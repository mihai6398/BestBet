using BestBet.Services.Scanner;

namespace BestBet.Contracts;

// The Polymarket scanner's side of the API. Every price is a probability between 0 and 1, every
// odds figure is decimal, and volumes and liquidity are in US dollars (USDC).

public enum ScannerSport
{
    Football,
    Tennis,
}

/// <summary>Which volume a match is measured, filtered and sorted by.</summary>
public enum VolumeBasis
{
    /// <summary>
    /// The match's headline event — the one that holds the 1X2 or the match winner, and the number
    /// Polymarket shows next to the match. The default.
    /// </summary>
    Event,

    /// <summary>
    /// The winner markets alone. For football this is the headline event's own volume (it holds
    /// nothing else); for tennis it leaves out the set, game and handicap markets.
    /// </summary>
    Moneyline,

    /// <summary>
    /// Every event that shares the match's <c>gameId</c> — the headline, More Markets, exact score,
    /// half-time… A More Markets event can out-trade the 1X2 several times over.
    /// </summary>
    Game,
}

public enum ScannerSort
{
    Volume,
    Start,
    Liquidity,
    Spread,
}

/// <summary>The markets the scanner reads. Football first, then tennis.</summary>
public enum ScannerMarketKind
{
    /// <summary>Football 1X2 (three binary markets) or a tennis match winner (one).</summary>
    Moneyline,
    /// <summary>Football total goals.</summary>
    Totals,
    BothTeamsToScore,
    /// <summary>Football handicap, e.g. Liverpool -1.5.</summary>
    Spread,
    /// <summary>Tennis total games in the match.</summary>
    TotalGames,
    /// <summary>Tennis total sets.</summary>
    TotalSets,
    SetHandicap,
    GameHandicap,
}

/// <summary>Where a match's totals were found, in the order they are looked for.</summary>
public enum TotalsSource
{
    /// <summary>a) In the match's own event, by <c>sportsMarketType</c> and <c>line</c>.</summary>
    Event,
    /// <summary>b) In another event with the same <c>gameId</c> — football's More Markets.</summary>
    Game,
    /// <summary>c) In the event the headline's slug plus <c>-more-markets</c> names.</summary>
    Slug,
    /// <summary>c) Through the search, matched to the match by both teams and the kick-off.</summary>
    Search,
}

/// <summary>
/// <c>GET /api/scanner/matches</c>. Every field is optional; unset thresholds come from
/// <c>Scanner</c> in appsettings.
/// </summary>
/// <remarks>
/// The window is two instants, not "today": what today means depends on where the user is, and the
/// browser knows that while the server does not.
/// </remarks>
public class ScannerQuery
{
    /// <summary>Both when empty.</summary>
    public ScannerSport[]? Sports { get; set; }

    /// <summary>Kick-off from, UTC. Defaults to now.</summary>
    public DateTime? From { get; set; }

    /// <summary>Kick-off until, UTC. Defaults to 48 hours from now.</summary>
    public DateTime? To { get; set; }

    /// <summary>Also matches already being played. On by default.</summary>
    public bool? IncludeLive { get; set; }

    public double? MinVolumeFootball { get; set; }
    public double? MinVolumeTennis { get; set; }
    public VolumeBasis? VolumeBasis { get; set; }

    /// <summary>Minimum resting liquidity of the match's headline event, in dollars.</summary>
    public double? MinLiquidity { get; set; }

    /// <summary>Largest bid/ask spread allowed on the match's winner markets, as a price (0.02 = 2¢).</summary>
    public double? MaxSpread { get; set; }

    /// <summary>A league or tournament, matched case-insensitively against its name.</summary>
    public string? League { get; set; }

    public ScannerSort? Sort { get; set; }
    public bool? Descending { get; set; }
}

/// <summary>A taker fee as the market states it.</summary>
public record FeeInfo(double Rate, double Exponent);

/// <summary>
/// One outcome, priced the way a bookmaker would show it.
/// </summary>
/// <param name="Label">What the column says: "1", "X", "2", "Over", "Under", a player's name.</param>
/// <param name="Name">The full side: the team, "Draw", "Over 2.5".</param>
/// <param name="BestAsk">What backing it costs per share. Every "back" figure comes from this.</param>
/// <param name="BackOdds">1 / <paramref name="BestAsk"/>.</param>
/// <param name="MidOdds">1 / the mid — informative only; nobody will fill you there.</param>
/// <param name="NetBackOdds">The back odds once the market's taker fee is paid.</param>
/// <param name="AskSize">Shares resting at the best ask.</param>
/// <param name="AskLiquidity">Dollars that go on at the best ask without moving it.</param>
/// <param name="FairProbability">This outcome's share of the market's back prices, margin removed.</param>
public record OutcomeQuote(
    string Label,
    string Name,
    string TokenId,
    string MarketId,
    string? ConditionId,
    double? BestBid,
    double? BestAsk,
    double? Mid,
    double? Spread,
    double? BackOdds,
    double? MidOdds,
    double? NetBackOdds,
    double? AskSize,
    double? AskLiquidity,
    double? FairProbability,
    PriceSource PriceSource);

/// <summary>
/// One market as the bookmaker would list it: the 1X2, one totals line, one handicap.
/// </summary>
/// <param name="Overround">Sum of the outcomes' back prices: 1.00 is a fair book, above it is margin.</param>
/// <param name="Question">Polymarket's wording, for a 1X2 the first of the three.</param>
public record ScannerMarket(
    ScannerMarketKind Kind,
    string Label,
    double? Line,
    IReadOnlyList<OutcomeQuote> Outcomes,
    double? Overround,
    double? Volume,
    double? Liquidity,
    FeeInfo? Fee,
    string? Question);

/// <param name="Id">The headline event's Gamma id: what <c>/api/scanner/matches/{id}</c> takes.</param>
/// <param name="League">"Premier League", "ATP Tour".</param>
/// <param name="Tournament">Tennis only: the part of the title before the colon, "Lyon Open".</param>
/// <param name="Volume">The volume on the basis the list was asked for.</param>
/// <param name="GameVolume">Null when the match's other events were not read.</param>
/// <param name="Liquidity">The headline event's resting liquidity.</param>
/// <param name="Spread">The widest bid/ask spread on the winner markets.</param>
/// <param name="MainTotal">The totals line closest to even money.</param>
/// <param name="TotalsSource">How the totals were found; null when they were not.</param>
public record ScannerMatch(
    string Id,
    long? GameId,
    ScannerSport Sport,
    string League,
    string? Tournament,
    string Title,
    string Home,
    string Away,
    DateTime StartTimeUtc,
    bool IsLive,
    string? Score,
    string? Period,
    double Volume,
    double EventVolume,
    double MoneylineVolume,
    double? GameVolume,
    double Liquidity,
    double? Spread,
    ScannerMarket? Moneyline,
    ScannerMarket? MainTotal,
    TotalsSource? TotalsSource,
    int TotalLines,
    string Url);

/// <param name="Truncated">More matches were listed than the page limit lets the scanner read.</param>
/// <param name="Warnings">What could not be read this time, in words.</param>
public record ScannerResult(
    IReadOnlyList<ScannerMatch> Matches,
    DateTime GeneratedUtc,
    VolumeBasis VolumeBasis,
    double MinVolumeFootball,
    double MinVolumeTennis,
    bool Truncated,
    IReadOnlyList<string> Warnings);

/// <param name="Markets">Every market the scanner prices for this match, the winner market first.</param>
/// <param name="TotalsTried">The paths tried for the totals, in order, for when there are none.</param>
public record ScannerMatchDetail(
    ScannerMatch Match,
    IReadOnlyList<ScannerMarket> Markets,
    IReadOnlyList<string> TotalsTried,
    DateTime GeneratedUtc);

public record BookLevel(double Price, double Size);

/// <summary>One token's book, best first on both sides, with running depth in dollars.</summary>
public record OrderBookDto(
    string TokenId,
    IReadOnlyList<BookLevel> Bids,
    IReadOnlyList<BookLevel> Asks,
    double? BestBid,
    double? BestAsk,
    double? Mid,
    double? Spread,
    double? TickSize,
    double? MinOrderSize,
    DateTime? TimestampUtc);

public record PricePoint(DateTime Time, double Price);

/// <summary>A move of at least the configured size within the configured window.</summary>
public record PriceMove(DateTime From, DateTime To, double FromPrice, double ToPrice);

/// <param name="Opening">The first price on record, which is as close to an opening line as there is.</param>
public record PriceHistoryDto(
    string TokenId,
    string Interval,
    IReadOnlyList<PricePoint> Points,
    PricePoint? Opening,
    PricePoint? Latest,
    IReadOnlyList<PriceMove> BigMoves,
    double BigMoveThreshold);

/// <summary>A match whose totals could not be found, kept so the mapping can be checked.</summary>
public record UnmappedTotals(
    DateTime AtUtc,
    ScannerSport Sport,
    string Match,
    string? Slug,
    long? GameId,
    IReadOnlyList<string> Tried);
