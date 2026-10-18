using System.Globalization;
using System.Text.Json;
using BestBet.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace BestBet.Services;

/// <summary>Thrown when the data source cannot be reached, refuses the key or the season, or answers nonsense.</summary>
public class PreMatchSourceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One team's row in a competition's table.</summary>
public record StandingsTeam(int Id, string Name, TeamForm Form);

/// <summary>A competition's table: every team's form, and the competition's own scoring averages.</summary>
public record Standings(IReadOnlyList<StandingsTeam> Teams, LeagueRates? League);

/// <summary>
/// Reads team form and player statistics from API-Football (api-sports.io).
/// </summary>
/// <remarks>
/// This is the second thing in the app that talks to the internet, and the only one that needs an
/// account. It needs one because there is no public source of player statistics that will answer a
/// server (Sofascore answers every non-browser request with 403). What leaves the machine is narrow:
/// a competition id, a season, a team id and an API key that never reaches the browser. No bankroll,
/// stake, odds or saved position is ever part of a request.
///
/// A free key allows 100 requests a day, so everything is cached: a table is one request for both
/// teams and the whole competition's averages, a squad is two or three, and both are kept for hours.
/// Analysing a dozen matches costs a handful of requests, not hundreds.
/// </remarks>
public class ApiFootballClient(HttpClient http, IMemoryCache cache, string? apiKey)
{
    public const string KeyHeader = "x-apisports-key";

    private static readonly TimeSpan StandingsCacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan SquadCacheFor = TimeSpan.FromHours(12);

    /// <summary>Squads are listed 20 players a page. Three pages cover a full senior squad.</summary>
    private const int MaxSquadPages = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>The competition's table, with every team's home and away record.</summary>
    public async Task<Standings> GetStandingsAsync(int leagueId, int season, CancellationToken cancellationToken)
    {
        var key = $"apifootball:standings:{leagueId}:{season}";
        if (cache.TryGetValue(key, out Standings? cached) && cached is not null)
        {
            return cached;
        }

        using var doc = await GetAsync($"standings?league={leagueId}&season={season}", cancellationToken);
        var standings = ReadStandings(doc.RootElement);

        // An empty table is not cached: it is what a season that has not started, or one the plan
        // cannot see, looks like, and caching it would hide the fix for half a day.
        if (standings.Teams.Count > 0)
        {
            cache.Set(key, standings, StandingsCacheFor);
        }

        return standings;
    }

    /// <summary>A team's players with their season so far, summed over every competition.</summary>
    public async Task<IReadOnlyList<PlayerSeason>> GetSquadAsync(
        int teamId,
        int season,
        CancellationToken cancellationToken)
    {
        var key = $"apifootball:squad:{teamId}:{season}";
        if (cache.TryGetValue(key, out IReadOnlyList<PlayerSeason>? cached) && cached is not null)
        {
            return cached;
        }

        var players = new List<PlayerSeason>();
        var totalPages = 1;

        for (var page = 1; page <= Math.Min(totalPages, MaxSquadPages); page++)
        {
            using var doc = await GetAsync($"players?team={teamId}&season={season}&page={page}", cancellationToken);

            if (doc.RootElement.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("total", out var total)
                && total.ValueKind == JsonValueKind.Number)
            {
                totalPages = total.GetInt32();
            }

            players.AddRange(ReadPlayers(doc.RootElement));
        }

        if (players.Count > 0)
        {
            cache.Set(key, (IReadOnlyList<PlayerSeason>)players, SquadCacheFor);
        }

        return players;
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new PreMatchSourceException(
                "No API-Football key is set. Add one as ApiFootball:Key (environment variable " +
                "ApiFootball__Key) and restart the app.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(KeyHeader, apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PreMatchSourceException("API-Football did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new PreMatchSourceException("API-Football could not be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new PreMatchSourceException(
                    $"API-Football answered {(int)response.StatusCode}. Check the key and the daily quota.");
            }

            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new PreMatchSourceException("API-Football answered something that is not JSON.", ex);
            }

            // The service reports most failures — a bad key, an exhausted quota, a season the plan
            // cannot see — with a 200 and an "errors" object, so the status code alone says nothing.
            var error = ReadError(doc.RootElement);
            if (error is not null)
            {
                doc.Dispose();
                throw new PreMatchSourceException($"API-Football: {error}");
            }

            return doc;
        }
    }

    /// <summary>The "errors" member is an empty array when all is well, and an object of messages when not.</summary>
    public static string? ReadError(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors))
        {
            return null;
        }

        var messages = errors.ValueKind switch
        {
            JsonValueKind.Object => errors.EnumerateObject().Select(p => p.Value.ToString()),
            JsonValueKind.Array => errors.EnumerateArray().Select(e => e.ToString()),
            _ => [],
        };

        var text = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Turns a standings response into per-team form and the competition's scoring averages.
    /// </summary>
    /// <remarks>
    /// A league is one group; a cup phase can be several, each its own array, so every group is read.
    /// </remarks>
    public static Standings ReadStandings(JsonElement root)
    {
        var teams = new List<StandingsTeam>();
        double homeGoals = 0, awayGoals = 0;
        int homeGames = 0, awayGames = 0;

        foreach (var entry in Items(root, "response"))
        {
            if (!entry.TryGetProperty("league", out var league) || !league.TryGetProperty("standings", out var groups))
            {
                continue;
            }

            foreach (var group in groups.EnumerateArray())
            {
                foreach (var row in group.EnumerateArray())
                {
                    if (!row.TryGetProperty("team", out var team)
                        || !row.TryGetProperty("home", out var home)
                        || !row.TryGetProperty("away", out var away))
                    {
                        continue;
                    }

                    var id = Int(team, "id");
                    var name = Str(team, "name");
                    var homePlayed = Int(home, "played");
                    var awayPlayed = Int(away, "played");

                    var homeFor = Int(home, "goals", "for");
                    var homeAgainst = Int(home, "goals", "against");
                    var awayFor = Int(away, "goals", "for");
                    var awayAgainst = Int(away, "goals", "against");

                    homeGoals += homeFor;
                    awayGoals += awayFor;
                    homeGames += homePlayed;
                    awayGames += awayPlayed;

                    teams.Add(new StandingsTeam(
                        id,
                        name,
                        new TeamForm(
                            homePlayed,
                            awayPlayed,
                            PerGame(homeFor, homePlayed),
                            PerGame(homeAgainst, homePlayed),
                            PerGame(awayFor, awayPlayed),
                            PerGame(awayAgainst, awayPlayed))));
                }
            }
        }

        // Too few games to be a rate at all (a season just begun): better to say nothing and let the
        // caller fall back than to quote 3.0 goals a game off one result.
        var league2 = homeGames >= 10 && awayGames >= 10
            ? new LeagueRates(homeGoals / homeGames, awayGoals / awayGames)
            : null;

        return new Standings(teams, league2);
    }

    /// <summary>
    /// Turns a players response into one row per player, summing his competitions.
    /// </summary>
    public static IReadOnlyList<PlayerSeason> ReadPlayers(JsonElement root)
    {
        var players = new List<PlayerSeason>();

        foreach (var entry in Items(root, "response"))
        {
            if (!entry.TryGetProperty("player", out var player) || !entry.TryGetProperty("statistics", out var stats))
            {
                continue;
            }

            int appearances = 0, minutes = 0, goals = 0, shots = 0;
            string? position = null;
            var bestMinutes = -1;

            foreach (var line in stats.EnumerateArray())
            {
                // "appearences" is the service's own spelling; the correct one is accepted too in case
                // it is ever fixed.
                appearances += Int(line, "games", "appearences") + Int(line, "games", "appearances");

                var lineMinutes = Int(line, "games", "minutes");
                minutes += lineMinutes;
                goals += Int(line, "goals", "total");
                shots += Int(line, "shots", "total");

                // The position he plays most is the one that describes him.
                if (lineMinutes > bestMinutes && Str(line, "games", "position") is { Length: > 0 } pos)
                {
                    position = pos;
                    bestMinutes = lineMinutes;
                }
            }

            var id = Int(player, "id");
            var name = Str(player, "name");
            if (id != 0 && name.Length > 0)
            {
                players.Add(new PlayerSeason(id, name, position, appearances, minutes, goals, shots));
            }
        }

        return players;
    }

    private static double PerGame(int total, int games) => games > 0 ? total / (double)games : 0.0;

    private static IEnumerable<JsonElement> Items(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    /// <summary>Walks a path of members and reads an integer, or 0 where anything along it is missing or null.</summary>
    private static int Int(JsonElement element, params string[] path)
    {
        var current = Walk(element, path);
        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(current.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => 0,
        };
    }

    private static string Str(JsonElement element, params string[] path)
    {
        var current = Walk(element, path);
        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    private static JsonElement Walk(JsonElement element, string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return default;
            }
        }

        return current;
    }
}
