using System.Text.Json;
using System.Text.Json.Serialization;
using BestBet.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace BestBet.Services;

/// <summary>Thrown when the exchange could not be reached or answered with something unusable.</summary>
public class ReferenceSourceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads markets off Polymarket's public Gamma API.
///
/// This is the first thing in the app that goes out to the internet on its own, and that is a real
/// change in character: the screenshot reader, the stats parser and the odds parser all work on a
/// machine with the network unplugged. So it is kept narrow and visible — one documented public
/// endpoint, a search string the user typed, a short timeout, nothing sent that was not asked for,
/// and no key or account anywhere. If it is unreachable the rest of the app carries on unchanged.
///
/// Why Polymarket is worth the exception: a prediction market has no bookmaker's margin to remove.
/// Its prices add up to about 1.00, so what it says is already a fair probability, which is exactly
/// the number this app otherwise has to reconstruct from a book that is 5–15% wide.
/// </summary>
public class PolymarketClient(HttpClient http, IMemoryCache cache)
{
    public const string DisplayName = "Polymarket";

    /// <summary>The documented public search endpoint. No authentication, no account.</summary>
    private const string SearchPath = "public-search?limit_per_type=12&q=";

    /// <summary>Looks one event up by its slug. Returns a JSON array, empty when there is none.</summary>
    private const string EventsPath = "events?slug=";

    /// <summary>The sibling event holding a match's totals, both-teams-to-score and handicap markets.</summary>
    private const string MoreMarketsSuffix = "-more-markets";

    /// <summary>How many matches get their sibling event fetched — the most traded ones.</summary>
    private const int MaxMatchesExpanded = 3;

    /// <summary>Market kinds taken from the sibling event; Polymarket tags each one itself.</summary>
    private static readonly HashSet<string> ExpandedKinds = ["totals", "both_teams_to_score", "spreads"];

    /// <summary>
    /// Long enough that the same match can be looked at repeatedly while a half plays out, short
    /// enough that a price moving during it still shows up before it matters.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    /// <summary>A market this thin is noise dressed as a consensus, so it is not offered at all.</summary>
    public const double MinimumVolume = 1000.0;

    public async Task<IReadOnlyList<PredictionMarketDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length < 3)
        {
            throw new ReferenceSourceException("Give it at least three characters to search for.");
        }

        var key = $"polymarket:{trimmed.ToLowerInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<PredictionMarketDto>? cached) && cached is not null)
        {
            return cached;
        }

        var found = (await FetchAsync(trimmed, cancellationToken))?.Events ?? [];
        var markets = ReadMarkets(found, await FetchMoreMarketsAsync(found, cancellationToken));
        cache.Set(key, markets, CacheFor);
        return markets;
    }

    /// <summary>
    /// The search only returns a match's headline event, which holds the three 1X2 markets. Totals,
    /// both-teams-to-score and handicaps live in a sibling event whose slug is the headline slug plus
    /// <c>-more-markets</c>, and search never surfaces it — so it is fetched by slug.
    /// </summary>
    /// <remarks>
    /// Best effort by design: if a sibling cannot be fetched the 1X2 markets are still worth showing,
    /// so a failure here costs the extra markets and nothing else. Only the few most traded matches
    /// are expanded, which keeps the number of outbound requests small and bounded.
    /// </remarks>
    private async Task<List<GammaEvent>> FetchMoreMarketsAsync(
        List<GammaEvent> found,
        CancellationToken cancellationToken)
    {
        var slugs = found
            .Where(e => !string.IsNullOrWhiteSpace(e.Slug)
                        && (e.Markets ?? []).Any(m => m.SportsMarketType == "moneyline" && !m.Closed))
            .OrderByDescending(e => (e.Markets ?? []).Sum(m => m.VolumeNum ?? 0.0))
            .Take(MaxMatchesExpanded)
            .Select(e => e.Slug! + MoreMarketsSuffix)
            .ToList();

        var fetched = await Task.WhenAll(slugs.Select(slug => FetchEventAsync(slug, cancellationToken)));
        return fetched.Where(e => e is not null).Select(e => e!).ToList();
    }

    private async Task<GammaEvent?> FetchEventAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(
                EventsPath + Uri.EscapeDataString(slug),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var events = await response.Content.ReadFromJsonAsync<List<GammaEvent>>(Json, cancellationToken);
            return events?.FirstOrDefault();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<SearchResponse?> FetchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(
                SearchPath + Uri.EscapeDataString(query),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new ReferenceSourceException(
                    $"Polymarket answered {(int)response.StatusCode}. Nothing is wrong with your numbers — " +
                    "the reference is simply unavailable right now.");
            }

            return await response.Content.ReadFromJsonAsync<SearchResponse>(Json, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReferenceSourceException("Polymarket did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new ReferenceSourceException("Could not reach Polymarket. Check the connection.", ex);
        }
        catch (JsonException ex)
        {
            throw new ReferenceSourceException("Polymarket answered with something this app cannot read.", ex);
        }
    }

    /// <summary>
    /// Flattens the events-with-markets response down to the markets that could actually be used.
    /// </summary>
    private static List<PredictionMarketDto> ReadMarkets(
        List<GammaEvent> found,
        List<GammaEvent> moreMarkets)
    {
        var results = new List<PredictionMarketDto>();

        // The sibling events carry dozens of markets nobody bets on here (exact score, team totals,
        // half-time lines), so only the kinds that map onto a form this app has are let through.
        var wanted = found.Select(e => (Event: e, OnlyKinds: false))
            .Concat(moreMarkets.Select(e => (Event: e, OnlyKinds: true)));

        foreach (var (evt, onlyKinds) in wanted)
        {
            foreach (var market in evt.Markets ?? [])
            {
                if (market.Closed || market.Archived || !market.Active)
                {
                    continue;
                }

                if (onlyKinds && !ExpandedKinds.Contains(market.SportsMarketType ?? string.Empty))
                {
                    continue;
                }

                var outcomes = ReadOutcomes(market);
                if (outcomes is null)
                {
                    continue;
                }

                if ((market.VolumeNum ?? 0.0) < MinimumVolume)
                {
                    continue;
                }

                results.Add(new PredictionMarketDto(
                    market.Id ?? evt.Slug ?? market.Question ?? string.Empty,
                    market.Question ?? evt.Title ?? string.Empty,
                    MatchTitle(evt.Title),
                    evt.Slug is null ? null : $"https://polymarket.com/event/{evt.Slug}",
                    market.VolumeNum,
                    market.LiquidityNum,
                    market.EndDate,
                    outcomes,
                    market.SportsMarketType,
                    market.Line));
            }
        }

        // Matches with the most money behind them first, and within one match the 1X2 markets, then
        // the totals by ascending line, then the rest — so "Over 2.5" sits next to "Over 3.5" instead
        // of being scattered by volume. A match's weight is its headline 1X2 volume, since that is
        // the number that says how much the prices are worth listening to.
        var matchVolume = results
            .GroupBy(m => m.EventTitle)
            .ToDictionary(g => g.Key, g => g.Max(m => m.Volume ?? 0.0));

        return results
            .OrderByDescending(m => matchVolume[m.EventTitle])
            .ThenBy(m => m.EventTitle, StringComparer.Ordinal)
            .ThenBy(m => KindRank(m.MarketType))
            .ThenBy(m => m.Line ?? 0.0)
            .ThenByDescending(m => m.Volume ?? 0.0)
            .ToList();
    }

    /// <summary>"A vs. B - More Markets" is the same match as "A vs. B", and should read as one.</summary>
    private static string MatchTitle(string? title)
    {
        var text = title ?? string.Empty;
        return text.EndsWith(" - More Markets", StringComparison.Ordinal)
            ? text[..^" - More Markets".Length]
            : text;
    }

    private static int KindRank(string? kind) => kind switch
    {
        "moneyline" => 0,
        "totals" => 1,
        "both_teams_to_score" => 2,
        "spreads" => 3,
        _ => 4,
    };

    /// <summary>
    /// Pulls the outcome names and prices out, or null when this market cannot serve as a reference.
    /// </summary>
    /// <remarks>
    /// Gamma returns both arrays as JSON encoded inside a JSON string, so they are parsed twice.
    /// A settled market reports 0 and 1, which would de-vig to a certainty and hand the analysis a
    /// 100% probability for something that already happened — so those are dropped rather than shown.
    /// </remarks>
    private static List<PredictionOutcome>? ReadOutcomes(GammaMarket market)
    {
        var labels = ParseEmbeddedArray(market.Outcomes);
        var prices = ParseEmbeddedArray(market.OutcomePrices);

        if (labels is null || prices is null || labels.Length != prices.Length || labels.Length < 2)
        {
            return null;
        }

        var outcomes = new List<PredictionOutcome>(labels.Length);

        for (var i = 0; i < labels.Length; i++)
        {
            if (!double.TryParse(prices[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var probability))
            {
                return null;
            }

            // A price at either extreme is a resolved or untraded market, and 1 / 0 is not a price.
            if (probability <= 0.001 || probability >= 0.999)
            {
                return null;
            }

            outcomes.Add(new PredictionOutcome(labels[i], probability, 1.0 / probability));
        }

        return outcomes;
    }

    private static string[]? ParseEmbeddedArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // --- The shape of the Gamma response, narrowed to what is used -----------

    private sealed class SearchResponse
    {
        [JsonPropertyName("events")]
        public List<GammaEvent>? Events { get; set; }
    }

    private sealed class GammaEvent
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public List<GammaMarket>? Markets { get; set; }
    }

    private sealed class GammaMarket
    {
        public string? Id { get; set; }
        public string? Question { get; set; }

        /// <summary>A JSON array inside a string, e.g. <c>["Yes", "No"]</c>.</summary>
        public string? Outcomes { get; set; }

        /// <summary>A JSON array inside a string, e.g. <c>["0.63", "0.37"]</c>.</summary>
        public string? OutcomePrices { get; set; }

        /// <summary>Polymarket's own tag: "moneyline", "totals", "both_teams_to_score", "spreads"…</summary>
        public string? SportsMarketType { get; set; }

        /// <summary>The goal line for totals and spreads, e.g. 2.5.</summary>
        public double? Line { get; set; }

        public bool Active { get; set; }
        public bool Closed { get; set; }
        public bool Archived { get; set; }
        public double? VolumeNum { get; set; }
        public double? LiquidityNum { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
