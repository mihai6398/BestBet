using BestBet.Data.Entities;
using BestBet.Domain;

namespace BestBet.Contracts;

/// <summary>Body of <c>POST /api/positions</c> — saves an analysis the user decided to keep.</summary>
public record SavePositionRequest(
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
    string? Notes);

/// <summary>
/// Body of <c>PATCH /api/positions/{id}</c>. Every field is optional: this endpoint exists so the
/// user can come back later and correct what was taken, fill in the closing price, and record the
/// result.
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
/// </remarks>
public record UpdatePositionRequest(
    double? ClosingDecimalOdds = null,
    double[]? ClosingSiblingOdds = null,
    PositionOutcome? Outcome = null,
    string? Notes = null,
    double? EntryDecimalOdds = null,
    double[]? EntrySiblingOdds = null,
    double? StakeAmount = null);

/// <summary>
/// Body of <c>POST /api/positions/{id}/reprice</c> — where the match is now.
/// </summary>
/// <param name="LiveState">Current minute, xG and score. Pre-match state is rejected: there is no
/// model to run without a clock.</param>
/// <param name="Pace">Same meaning as on an analysis. Omit it for the corrected model.</param>
public record RepricePositionRequest(LiveMatchState LiveState, PaceSettings? Pace = null);

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
/// <param name="ExpectedValuePerUnit">At the price already taken — the only price that can still apply.</param>
/// <param name="ExpectedValueAmount">The same, in currency, on the stake actually placed.</param>
/// <param name="FairOddsNow">1 / probability: what a fair price would be at this moment, which is
/// the number to compare a cash-out or a hedge against.</param>
public record RepricedPosition(
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
    double FairOddsNow,
    ProbabilityEstimate Estimate,
    ModelDiagnostics Model);

/// <summary>One row of the positions log, with the CLV numbers already worked out.</summary>
public record PositionDto(
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
    PositionOutcome Outcome,
    string? Notes,
    double? ClvPercent,
    bool? BeatTheClose,
    double? ClvProbabilityPoints,
    double? ProfitLoss);

/// <summary>
/// The scoreboard for the process itself. The target from the strategy is beating the close on 55%+
/// of positions over 30+ of them — below that sample size these numbers are not yet evidence.
/// </summary>
public record PositionsSummary(
    int TotalPositions,
    int PositionsWithClosingPrice,
    int PositionsBeatingClose,
    double? BeatCloseRate,
    double? AverageClvPercent,
    int SettledPositions,
    double TotalStaked,
    double TotalProfitLoss,
    string Readout);
