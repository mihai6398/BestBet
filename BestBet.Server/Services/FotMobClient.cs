using System.Globalization;
using System.Text.Json;
using BestBet.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace BestBet.Services;

/// <summary>Thrown when a live source could not be reached, or answered with something unusable.</summary>
public class LiveSourceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads a match in progress off FotMob's public API, from a link or an id the user pasted.
///
/// This exists because the honest answer to "why do I have to run a bookmarklet" turned out to be
/// "only for Sofascore". Sofascore answers a programmatic request with 403 — every request, down to
/// robots.txt — so there is no server-side route to them that is not an impersonation. FotMob
/// answers 200 with no key, no account and no pretending: two documented paths under
/// <c>/api/data/</c>, and the fields this app needs are in them.
///
/// So the bookmarklet is no longer the only way in, and it is also not retired. FotMob does not
/// compute xG for every league — it is there for the Bundesliga and absent for, say, the Copa
/// Libertadores Femenina — and without xG this app's model has nothing to run on. A match like that
/// comes back with its xG reported missing rather than filled in with a plausible number, and the UI
/// sends the user back to the bookmarklet or to a manual paste. Inventing the one field the whole
/// analysis rests on would be the single worst thing this class could do.
/// </summary>
public class FotMobClient(HttpClient http, IMemoryCache cache)
{
    public const string DisplayName = "FotMob";

    /// <summary>
    /// How long one reading is reused.
    /// </summary>
    /// <remarks>
    /// The browser polls every <c>POLL_INTERVAL_SECONDS</c> (8s), which is the right cadence for
    /// asking our own API and far too eager for someone else's. FotMob's numbers do not move faster
    /// than this anyway, so the cache costs no real freshness and keeps the outbound request rate in
    /// the same range the bookmarklet already pushes at. A served-from-cache reading still reports
    /// its true age, so nothing downstream mistakes it for having just been taken.
    /// </remarks>
    private static readonly TimeSpan CacheReadingFor = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A whole day's fixture list is a quarter of a megabyte and changes only as matches kick off,
    /// so the projection of it is held long enough that resolving a Sofascore link does not
    /// re-download the day on every attempt.
    /// </summary>
    private static readonly TimeSpan CacheDayFor = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A match's id never changes, so once a Sofascore link has been resolved the answer is good for
    /// as long as the app runs — which keeps the expensive lookup to once per link.
    /// </summary>
    private static readonly TimeSpan CacheResolutionFor = TimeSpan.FromHours(6);

    /// <summary>
    /// Reads the current state of the match the input points at.
    /// </summary>
    /// <param name="input">A FotMob id, a FotMob link, or a Sofascore link.</param>
    public async Task<ProviderReading> ReadAsync(string input, CancellationToken cancellationToken = default)
    {
        var reference = MatchReference.Parse(input)
                        ?? throw new LiveSourceException(
                            "That is not a match link this app can read. Paste a FotMob or Sofascore " +
                            "match link, or a FotMob match id.");

        var id = reference.Provider == MatchProvider.FotMob
            ? reference.FotMobId!
            : await ResolveByNameAsync(reference.TeamsLabel!, cancellationToken)
              ?? throw new LiveSourceException(
                  $"No match for “{reference.TeamsLabel!.Trim()}” was found at {DisplayName}. A Sofascore link only " +
                  $"gives up the team names, so the match has to be found at {DisplayName} by name — paste the " +
                  $"{DisplayName} link for the same match instead, or use the bookmarklet.");

        return await ReadIdAsync(id, cancellationToken);
    }

    /// <summary>
    /// Reads the match a saved bet is on, found by the bet's own "Home - Away" label.
    /// </summary>
    /// <remarks>
    /// This is what lets every open bet follow its own match, rather than only the one match the
    /// monitor was pointed at. A bet carries no provider id — it was saved from a form, a slip or a
    /// paste — but it carries the two team names, and those are exactly what a Sofascore link gives
    /// up too, so it is found the same way and under the same name-matching gate.
    /// </remarks>
    /// <returns>Null when no match by those names is listed in the three days around today.</returns>
    public async Task<ProviderReading?> ReadByTeamsAsync(string matchLabel, CancellationToken cancellationToken = default)
    {
        var id = await ResolveByNameAsync(matchLabel, cancellationToken);
        return id is null ? null : await ReadIdAsync(id, cancellationToken);
    }

    private async Task<ProviderReading> ReadIdAsync(string id, CancellationToken cancellationToken)
    {
        var key = $"fotmob:reading:{id}";

        if (!cache.TryGetValue(key, out ProviderReading? reading) || reading is null)
        {
            using var document = await FetchAsync($"matchDetails?matchId={id}", cancellationToken);
            reading = FotMobReading.From(document, id);
            cache.Set(key, reading, CacheReadingFor);
        }

        // Stamped on the way out rather than stored, because the same cached reading is a different
        // age to each caller — and its age is the number that decides whether to trust it.
        return reading with { AgeSeconds = (DateTime.UtcNow - reading.ReadUtc).TotalSeconds };
    }

    /// <summary>
    /// Finds the FotMob id of a match named only by its teams.
    /// </summary>
    /// <remarks>
    /// This is the whole price of accepting a Sofascore link. Their id means nothing outside their
    /// own API, so the match has to be found again from scratch, and the only handle their URL gives
    /// up is the team names in its slug.
    ///
    /// Three days are searched, not one: FotMob groups fixtures by UTC date, so a late kickoff is
    /// filed under yesterday while the user is watching it tonight, or under tomorrow when they are
    /// an hour behind UTC. A match in progress is preferred over one that merely matches, because
    /// the same two clubs meeting twice in a season is ordinary and only one of those games is on
    /// right now.
    ///
    /// The name matching is the gate, and it is the same rule the client uses to decide which saved
    /// bets a reading may re-price — see <see cref="MatchNameMatching"/>. Getting it wrong here is
    /// the expensive failure: a wrong id reads a real clock off a real match that is not the one the
    /// user is betting on, and nothing downstream could tell.
    /// </remarks>
    private async Task<string?> ResolveByNameAsync(string teams, CancellationToken cancellationToken)
    {
        var key = $"fotmob:resolved:{teams.ToLowerInvariant()}";
        if (cache.TryGetValue(key, out string? resolved) && resolved is not null)
        {
            return resolved;
        }

        var today = DateTime.UtcNow.Date;
        Candidate? best = null;

        foreach (var day in new[] { today, today.AddDays(-1), today.AddDays(1) })
        {
            foreach (var fixture in await DayAsync(day, cancellationToken))
            {
                if (!MatchNameMatching.SameMatch(teams, fixture.Label))
                {
                    continue;
                }

                // Ongoing outranks everything; among equals, the one sharing more of the names wins.
                // A single shared word is enough to be eligible but is the weakest evidence there
                // is, so it must never beat a fuller match found a day over.
                var rank = (fixture.Ongoing ? 100 : 0) + MatchNameMatching.SharedWords(teams, fixture.Label);

                if (best is null || rank > best.Value.Rank)
                {
                    best = new Candidate(fixture.Id, rank);
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        cache.Set(key, best.Value.Id, CacheResolutionFor);
        return best.Value.Id;
    }

    private readonly record struct Candidate(string Id, int Rank);

    /// <summary>One fixture, cut down to what picking the right match needs.</summary>
    /// <remarks>
    /// The day list is projected to this before it is cached, rather than the parsed document being
    /// kept. A <c>JsonDocument</c> rents pooled memory and has to be disposed exactly once, which a
    /// cache that hands the same instance to several callers and drops it on a timer cannot promise.
    /// Three fields per fixture is also a few kilobytes instead of a few hundred.
    /// </remarks>
    private readonly record struct Fixture(string Id, string Label, bool Ongoing);

    private async Task<IReadOnlyList<Fixture>> DayAsync(DateTime day, CancellationToken cancellationToken)
    {
        var stamp = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var key = $"fotmob:day:{stamp}";

        if (cache.TryGetValue(key, out IReadOnlyList<Fixture>? cached) && cached is not null)
        {
            return cached;
        }

        using var document = await FetchAsync($"matches?date={stamp}", cancellationToken);
        var fixtures = Fixtures(document);
        cache.Set(key, fixtures, CacheDayFor);
        return fixtures;
    }

    private static List<Fixture> Fixtures(JsonDocument day)
    {
        var fixtures = new List<Fixture>();

        foreach (var league in Items(day.RootElement, "leagues"))
        {
            foreach (var match in Items(league, "matches"))
            {
                var id = Id(match);
                var home = Text(Child(match, "home"), "name");
                var away = Text(Child(match, "away"), "name");

                if (id is null || home is null || away is null)
                {
                    continue;
                }

                var status = Child(match, "status");

                fixtures.Add(new Fixture(
                    id,
                    $"{home} - {away}",
                    (Flag(status, "started") ?? false) && !(Flag(status, "finished") ?? false)));
            }
        }

        return fixtures;
    }

    /// <summary>
    /// One GET against FotMob's public API, with every way it can fail turned into a sentence.
    /// </summary>
    /// <remarks>
    /// Note where the paths live: under <c>/api/data/</c>, which is the base address this client is
    /// configured with. The older <c>/api/matchDetails</c> answers 404 and is the easy thing to
    /// reach for out of habit.
    /// </remarks>
    private async Task<JsonDocument> FetchAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(path, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new LiveSourceException(
                    $"{DisplayName} answered {(int)response.StatusCode}. Nothing is wrong with your " +
                    "numbers — the match simply cannot be read right now. The bookmarklet still works.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LiveSourceException($"{DisplayName} did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new LiveSourceException($"Could not reach {DisplayName}. Check the connection.", ex);
        }
        catch (JsonException ex)
        {
            throw new LiveSourceException($"{DisplayName} answered with something this app cannot read.", ex);
        }
    }

    // --- Loose reads over the fixture list ----------------------------------

    /// <summary>The day list prints ids as numbers; <c>matchDetails</c> prints the same id as a string.</summary>
    private static string? Id(JsonElement match)
    {
        var value = Child(match, "id");

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child)
            ? child
            : default;

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name)
    {
        var array = Child(parent, name);
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
    }

    private static string? Text(JsonElement parent, string name)
    {
        var value = Child(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool? Flag(JsonElement parent, string name) => Child(parent, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
