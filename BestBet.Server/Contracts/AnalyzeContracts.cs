using BestBet.Domain;

namespace BestBet.Contracts;

/// <summary>
/// Body of <c>POST /api/analyze</c>.
/// </summary>
/// <param name="Market">The priced outcome plus its siblings, for the de-vig.</param>
/// <param name="LiveState">Live match state. Omit it, or set <see cref="LiveMatchState.IsPreMatch"/>,
/// for a pre-match analysis — in which case <paramref name="ManualOwnProbability"/> becomes required.</param>
/// <param name="Bankroll">Current bankroll in the user's own currency. Used only to turn a stake
/// fraction into an amount; it is never stored server-side.</param>
/// <param name="Selection">Which side of the market <see cref="MarketOdds.Label"/> refers to. Optional,
/// but strongly preferred: without it the label has to be parsed, and labels are free text.</param>
/// <param name="Line">Goal or handicap line, when it is not already in the label. Overrides the label.</param>
/// <param name="ManualOwnProbability">0..1. Required pre-match, ignored live.</param>
/// <param name="Pace">How the live xG rate should be projected forward. Omit it to get the corrected
/// model, which is what you want in almost every case; send PaceSettings.Flat to get the spec's raw
/// extrapolation back for comparison.</param>
/// <param name="ReferenceMarket">The SAME outcome priced somewhere else — a sharper book, or a
/// prediction market. When present, the fair probability is taken from here instead of from
/// <paramref name="Market"/>, while EV and the stake stay measured against the price in
/// <paramref name="Market"/>, because that is the only price actually on offer. Nothing checks that
/// the two describe the same outcome; that is the caller's job and the UI says so out loud.</param>
/// <param name="ReferenceSource">What to call the reference on screen, e.g. "Polymarket".</param>
/// <param name="PreMatch">Each side's expected goals from the pre-match model. When present on a match
/// that has not started, it replaces <paramref name="ManualOwnProbability"/>: the probability is
/// priced off these rates for whichever market and side the request names. On a live match it is the
/// target the pace is shrunk towards, in place of the league average.</param>
public record AnalyzeRequest(
    MarketOdds Market,
    LiveMatchState? LiveState,
    double Bankroll,
    MarketSelection? Selection = null,
    double? Line = null,
    double? ManualOwnProbability = null,
    PaceSettings? Pace = null,
    MarketOdds? ReferenceMarket = null,
    string? ReferenceSource = null,
    PreMatchRates? PreMatch = null);

/// <summary>
/// Expected goals for each side over the whole match, as produced by the pre-match model.
/// </summary>
public record PreMatchRates(double HomeExpectedGoals, double AwayExpectedGoals);

/// <summary>
/// A second opinion on the fair price, from a book that is not the one being bet into.
///
/// Why this is worth having its own slot rather than just replacing the odds: the bookmaker's own
/// de-vigged line and the price you can get are the same number wearing two hats, and a wide,
/// slow book is bad at the first job while being the only option for the second. A prediction
/// market prices at a margin near zero, so its de-vig is closer to the truth — but you cannot bet
/// your stake into it. Splitting the two is what lets the sharper line decide whether there is an
/// edge, while the stake is still sized against what you will actually be paid.
/// </summary>
/// <param name="Source">Where the reference came from, for the screen.</param>
/// <param name="FairProbability">De-vigged from the reference. This is what the edge is measured against.</param>
/// <param name="Overround">The reference book's own margin. Near 1.00 for a prediction market.</param>
/// <param name="BookFairProbability">What the bookmaker's own prices de-vig to, kept for comparison.</param>
/// <param name="DisagreementPoints">Reference minus bookmaker, in percentage points. Large and
/// positive means the bookmaker is pricing this outcome shorter than the sharper market does.</param>
public record ReferenceLine(
    string Source,
    double FairProbability,
    double Overround,
    double BookFairProbability,
    double DisagreementPoints);

/// <summary>
/// The model's working, exposed so the user can sanity-check the number instead of trusting it.
/// Null for pre-match analyses, where no model ran.
/// </summary>
public record ModelDiagnostics(
    int RemainingMinutes,
    double XgRateHomePerMinute,
    double XgRateAwayPerMinute,
    double ExpectedRemainingHome,
    double ExpectedRemainingAway,
    double RawWinProbability,
    double PushProbability,
    string ResolvedSelection,
    PaceAdjustment Pace);

/// <summary>
/// Response of <c>POST /api/analyze</c>. The first four fields are the contract from the spec;
/// the rest are additive detail the UI renders alongside them.
/// </summary>
public record AnalyzeResponse(
    EdgeResult Edge,
    StakeRecommendation Stake,
    Verdict Verdict,
    string VerdictReason,
    ProbabilityEstimate Estimate,
    double Overround,
    ModelDiagnostics? Model,
    string Disclaimer,
    ReferenceLine? Reference = null);
