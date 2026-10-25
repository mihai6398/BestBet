using System.Globalization;
using System.Text.Json;

namespace BestBet.Services.Scanner;

// The shape of Gamma's answers, narrowed to what the scanner reads. Written against the captured
// responses in BestBet.Server.Tests/Fixtures/polymarket-*.json, not the documentation.
//
// Dates stay strings and are parsed leniently: one malformed date in one event of a hundred must
// cost that event, not the whole page. "gameStartTime" is not even ISO ("2026-10-25 14:00:00+00").

/// <summary>One Gamma event: a match's headline, one of its sibling events, or a season-long market.</summary>
public sealed class GammaEvent
{
    public string? Id { get; set; }
    public string? Slug { get; set; }
    public string? Title { get; set; }

    /// <summary>Kick-off. Not <c>startDate</c>, which is when the event was created.</summary>
    public string? StartTime { get; set; }

    /// <summary>Kick-off for football, kick-off plus about a week for tennis.</summary>
    public string? EndDate { get; set; }

    /// <summary>Shared by every event of one match. Missing on ITF tennis and on older matches.</summary>
    public long? GameId { get; set; }

    public double? Volume { get; set; }
    public double? Liquidity { get; set; }
    public bool Active { get; set; }
    public bool Closed { get; set; }
    public bool? Live { get; set; }
    public bool? Ended { get; set; }

    /// <summary>"1-0" in football, "2-6, 2-2" in tennis. Only while live.</summary>
    public string? Score { get; set; }

    /// <summary>"HT", "S2"… Only while live.</summary>
    public string? Period { get; set; }

    public string? SeriesSlug { get; set; }
    public List<GammaTeam>? Teams { get; set; }
    public GammaSport? Sport { get; set; }
    public List<GammaMarket>? Markets { get; set; }

    public DateTime? StartUtc => GammaDates.Parse(StartTime);

    public DateTime? EndUtc => GammaDates.Parse(EndDate);
}

public sealed class GammaTeam
{
    public string? Name { get; set; }

    /// <summary>The short form, e.g. "Brighton" for "Brighton &amp; Hove Albion FC".</summary>
    public string? Alias { get; set; }

    public string? Abbreviation { get; set; }

    /// <summary>"home" or "away".</summary>
    public string? Ordering { get; set; }
}

/// <summary>The league the event belongs to: <c>{"sport": "epl", "name": "Premier League"}</c>.</summary>
public sealed class GammaSport
{
    public string? Sport { get; set; }
    public string? Name { get; set; }
}

public sealed class GammaMarket
{
    public string? Id { get; set; }
    public string? Question { get; set; }
    public string? ConditionId { get; set; }
    public string? Slug { get; set; }

    /// <summary>e.g. "O/U 2.5", "Liverpool FC", "Draw (Liverpool FC vs. …)", "AVL-MCI O/U 2.5".</summary>
    public string? GroupItemTitle { get; set; }

    /// <summary>Polymarket's own tag, from <c>/sports/market-types</c>. Missing on older markets.</summary>
    public string? SportsMarketType { get; set; }

    /// <summary>The line of a total or a spread, e.g. 2.5 or -1.5.</summary>
    public double? Line { get; set; }

    /// <summary>A JSON array inside a string, e.g. <c>["Over", "Under"]</c>.</summary>
    public string? Outcomes { get; set; }

    /// <summary>A JSON array inside a string, e.g. <c>["0.635", "0.365"]</c>.</summary>
    public string? OutcomePrices { get; set; }

    /// <summary>A JSON array inside a string: one CLOB token per outcome, in the same order.</summary>
    public string? ClobTokenIds { get; set; }

    public double? VolumeNum { get; set; }
    public double? LiquidityNum { get; set; }

    /// <summary>Best bid of the FIRST token only.</summary>
    public double? BestBid { get; set; }

    /// <summary>Best ask of the FIRST token only.</summary>
    public double? BestAsk { get; set; }

    public double? LastTradePrice { get; set; }
    public double? OrderPriceMinTickSize { get; set; }
    public double? OrderMinSize { get; set; }
    public bool Active { get; set; }
    public bool Closed { get; set; }
    public bool Archived { get; set; }
    public bool? AcceptingOrders { get; set; }

    /// <summary>"2026-10-25 14:00:00+00" — not ISO, hence a string.</summary>
    public string? GameStartTime { get; set; }

    public string? EndDate { get; set; }
    public bool? FeesEnabled { get; set; }
    public GammaFeeSchedule? FeeSchedule { get; set; }

    public string[] OutcomeLabels() => GammaArrays.Parse(Outcomes);

    public string[] TokenIds() => GammaArrays.Parse(ClobTokenIds);

    public DateTime? GameStartUtc => GammaDates.Parse(GameStartTime);

    /// <summary>A binary market with a token for each side, which is every market the scanner prices.</summary>
    public bool IsBinary => OutcomeLabels().Length == 2 && TokenIds().Length == 2;

    /// <summary>What a taker pays here, or <see cref="FeeModel.None"/> when the market charges nothing.</summary>
    public FeeModel Fee() =>
        FeesEnabled == true && FeeSchedule?.Rate is > 0.0 and var rate
            ? new FeeModel(rate, FeeSchedule.Exponent ?? 1.0)
            : FeeModel.None;
}

/// <summary><c>{"rate": 0.05, "exponent": 1, "takerOnly": true}</c> on a sports market.</summary>
public sealed class GammaFeeSchedule
{
    public double? Rate { get; set; }
    public double? Exponent { get; set; }
    public bool? TakerOnly { get; set; }
}

public sealed class GammaSearchResponse
{
    public List<GammaEvent>? Events { get; set; }
}

internal static class GammaArrays
{
    /// <summary>Gamma returns these arrays as JSON encoded inside a JSON string, so they are parsed twice.</summary>
    public static string[] Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

internal static class GammaDates
{
    private static readonly string[] GameStartFormats = ["yyyy-MM-dd HH:mm:sszz", "yyyy-MM-dd HH:mm:sszzz"];

    /// <summary>ISO dates and Gamma's "2026-10-25 14:00:00+00", always as UTC; null for anything else.</summary>
    public static DateTime? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(text, GameStartFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var exact))
        {
            return exact.UtcDateTime;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }
}
