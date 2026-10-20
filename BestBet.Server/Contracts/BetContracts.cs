using BestBet.Data.Entities;
using BestBet.Domain;

namespace BestBet.Contracts;

// Money and prices cross the wire as plain JSON numbers and are held as double here; the service
// rounds them into the decimal columns on the way in (two places for money, four for prices). The
// entity is never sent or received directly.

/// <summary>Body of <c>POST /api/bets</c> — saves a bet, usually straight from an analysis.</summary>
/// <param name="StakeAmount">Zero is allowed on purpose: an analysis that was not bet can still be
/// kept and judged on closing-line value, and a zero stake moves neither the balance nor the yield.</param>
public record SaveBetRequest(
    string MatchLabel,
    MarketKind MarketKind,
    string MarketLabel,
    string Selection,
    double EntryDecimalOdds,
    double[] EntrySiblingOdds,
    double EntryFairProbability,
    double EntryOwnProbability,
    double EntryEdgePercentagePoints,
    string EstimateMethod,
    double BankrollAtEntry,
    double StakeAmount,
    double StakeFraction,
    Verdict Verdict,
    bool WasLive,
    int? MinutePlayed,
    double? CumulativeXgHome,
    double? CumulativeXgAway,
    int? GoalsHome,
    int? GoalsAway,
    string? Notes,
    string? Sport = null,
    string? Competition = null,
    string? Bookmaker = null,
    DateTime? EventStartUtc = null);

/// <summary>
/// Body of <c>POST /api/bets/import</c> — a bet that was already placed, read off the bookmaker's
/// slip rather than produced by an analysis here.
/// </summary>
/// <remarks>
/// What a slip has is the price, the stake and the score; what it does not have is the other side of
/// the market, so there is nothing to de-vig and no honest "fair price at entry". The match state is
/// optional for the same reason in the other direction: with the minute, score and xG the model can
/// say what the bet was worth when it was taken, and without them the row is only a record that the
/// bet exists and can be tracked from here on.
/// </remarks>
/// <param name="Selection">The resolved side in the canonical form, e.g. "Under 2".</param>
public record ImportBetRequest(
    string MatchLabel,
    MarketKind MarketKind,
    string MarketLabel,
    string Selection,
    double EntryDecimalOdds,
    double StakeAmount,
    int? MinutePlayed = null,
    int? GoalsHome = null,
    int? GoalsAway = null,
    double? CumulativeXgHome = null,
    double? CumulativeXgAway = null,
    string? Notes = null,
    string? Bookmaker = null);

/// <summary>
/// Body of <c>PUT</c> or <c>PATCH /api/bets/{id}</c>. Every field is optional: this endpoint exists so
/// the user can come back later and correct what was taken, fill in the closing price, and describe
/// the bet better.
/// </summary>
/// <remarks>
/// The entry price and the stake are editable because the saved row is a record of what was
/// actually bet, not of what the analysis suggested. A live price can move between reading it and
/// clicking, the stake taken is often rounded, and a row that keeps the suggestion instead of the
/// fill would make every CLV number downstream measure the wrong thing.
///
/// Editing the entry price re-derives the fair probability and the edge from it, because those two
/// are functions of the price and would otherwise describe a bet nobody placed. The model's own
/// probability is left alone: it was the estimate at the time, and nothing about getting a worse
/// price changes what the match was doing.
///
/// A status sent here settles the bet exactly as <c>POST /api/bets/{id}/settle</c> does, payout and
/// profit included; <see cref="BetStatus.Open"/> reopens it.
/// </remarks>
public record UpdateBetRequest(
    double? ClosingDecimalOdds = null,
    double[]? ClosingSiblingOdds = null,
    BetStatus? Status = null,
    string? Notes = null,
    double? EntryDecimalOdds = null,
    double[]? EntrySiblingOdds = null,
    double? StakeAmount = null,
    string? MatchLabel = null,
    string? Sport = null,
    string? Competition = null,
    string? Bookmaker = null,
    DateTime? EventStartUtc = null,
    double? Payout = null);

/// <summary>Body of <c>POST /api/bets/{id}/settle</c>.</summary>
/// <param name="Status">How it ended. <see cref="BetStatus.Open"/> is refused here; use an update to reopen.</param>
/// <param name="Payout">Only for <see cref="BetStatus.CashedOut"/>, where it is required: what the
/// bookmaker paid back. Every other result has its payout worked out from the stake and the price.</param>
/// <param name="ClosingDecimalOdds">Optional, because settling is when the closing price is usually
/// to hand.</param>
public record SettleBetRequest(BetStatus Status, double? Payout = null, double? ClosingDecimalOdds = null);

/// <summary>
/// Body of <c>POST /api/bets/{id}/reprice</c> — where the match is now.
/// </summary>
/// <param name="LiveState">Current minute, xG and score. Pre-match state is rejected: there is no
/// model to run without a clock.</param>
/// <param name="Pace">Same meaning as on an analysis. Omit it for the corrected model.</param>
/// <param name="PreMatch">The match's pre-match expected goals, when the caller has them. Same meaning as
/// on a live analysis: the pace is shrunk towards these rather than the league average, so a bet is
/// re-priced on the same terms it was priced on.</param>
public record RepriceBetRequest(LiveMatchState LiveState, PaceSettings? Pace = null, PreMatchRates? PreMatch = null);

/// <summary>
/// What an already-placed bet is worth now.
/// </summary>
/// <remarks>
/// Nothing here is written back. The entry snapshot on the row is the record of what was known when
/// the stake went down, and overwriting it with a later reading would destroy the only evidence
/// that the decision was reasonable at the time. This is a question asked of the row, not a change
/// to it, which is also why it can be asked as often as the match changes.
/// </remarks>
/// <param name="OwnProbability">The model's probability that this bet wins, as of now.</param>
/// <param name="ProbabilityPoints">Now minus at entry, in percentage points.</param>
/// <param name="ExpectedValuePerUnit">At the price already taken — the only price that can still apply.
/// Over the whole stake: a returned stake is a zero, not a win.</param>
/// <param name="ExpectedValueAmount">The same, in currency, on the stake actually placed.</param>
/// <param name="FairOddsNow">1 / probability: what a fair price would be at this moment, which is
/// the number to compare a cash-out or a hedge against. Null once the score has decided the bet
/// against you: a price for something that cannot happen is infinite, and infinity is not JSON.</param>
public record RepricedBet(
    int Id,
    string MatchLabel,
    string Selection,
    double EntryDecimalOdds,
    double StakeAmount,
    double EntryOwnProbability,
    double OwnProbability,
    double ProbabilityPoints,
    double ExpectedValuePerUnit,
    double ExpectedValueAmount,
    double? FairOddsNow,
    ProbabilityEstimate Estimate,
    ModelDiagnostics Model);

/// <summary>One saved bet, with the CLV numbers already worked out.</summary>
public record BetDto(
    int Id,
    DateTime CreatedUtc,
    string MatchLabel,
    MarketKind MarketKind,
    string MarketLabel,
    string Selection,
    double EntryDecimalOdds,
    double[] EntrySiblingOdds,
    double EntryFairProbability,
    double EntryOwnProbability,
    double EntryEdgePercentagePoints,
    string EstimateMethod,
    double BankrollAtEntry,
    double StakeAmount,
    double StakeFraction,
    Verdict Verdict,
    bool WasLive,
    int? MinutePlayed,
    double? CumulativeXgHome,
    double? CumulativeXgAway,
    int? GoalsHome,
    int? GoalsAway,
    double? ClosingDecimalOdds,
    double[] ClosingSiblingOdds,
    BetStatus Status,
    string? Notes,
    double? ClvPercent,
    bool? BeatTheClose,
    double? ClvProbabilityPoints,
    double? ProfitLoss,
    double? Payout,
    DateTime? SettledUtc,
    string? Sport,
    string? Competition,
    string? Bookmaker,
    DateTime? EventStartUtc);

/// <summary>
/// The scoreboard: what the money did, and whether the process is finding real prices.
/// </summary>
/// <remarks>
/// The CLV target from the strategy is beating the close on 55%+ of bets over 30+ of them — below
/// that sample size those numbers are not yet evidence, and the readout says so.
/// </remarks>
/// <param name="TotalStaked">On settled bets only: an open stake has no result yet.</param>
/// <param name="Yield">Profit over the amount staked on settled bets. Null with nothing staked.</param>
/// <param name="Roi">Profit over the money put into the bankroll (deposits less withdrawals). Null
/// when nothing has been put in.</param>
/// <param name="OpenStake">Money currently riding on open bets.</param>
public record BetStats(
    int TotalBets,
    int OpenBets,
    double OpenStake,
    int SettledBets,
    double TotalStaked,
    double TotalProfitLoss,
    double? Yield,
    double? Roi,
    int BetsWithClosingPrice,
    int BetsBeatingClose,
    double? BeatCloseRate,
    double? AverageClvPercent,
    string Readout);

/// <summary>The query string of <c>GET /api/bets</c>.</summary>
/// <param name="Status">"open", "closed", or one status name such as "Won". Empty means every bet.</param>
/// <param name="Page">1-based. With no page, every matching bet comes back.</param>
/// <param name="PageSize">1 to <see cref="MaxPageSize"/>; defaults to 50 when a page is asked for.</param>
public record BetListQuery(string? Status = null, int? Page = null, int? PageSize = null)
{
    public const int MaxPageSize = 500;
}

/// <summary>One page of bets and how many match in total.</summary>
public record BetPage(IReadOnlyList<BetDto> Items, int Total);
