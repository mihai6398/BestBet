using System.Globalization;
using BestBet.Contracts;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Thrown when the request itself does not make sense. The controller turns this into a 400 with the
/// message intact, because a user typing odds into a form deserves to be told what is wrong.
/// </summary>
public class AnalysisInputException(string message) : Exception(message);

/// <summary>
/// Runs one analysis end to end: de-vig the market, estimate our own probability, compare the two,
/// size the stake, decide. The four services do the thinking; this wires them together in order.
/// </summary>
public class AnalysisService
{
    /// <summary>
    /// Shown on every result, in the API as well as the UI, so it survives anyone using the endpoint
    /// directly.
    /// </summary>
    public const string Disclaimer = "Educational decision-support only. Not financial or betting advice.";

    public AnalyzeResponse Analyze(AnalyzeRequest request)
    {
        Validate(request);

        var market = request.Market;
        var isLive = request.LiveState is { IsPreMatch: false };

        var (estimate, diagnostics) = isLive
            ? EstimateFromLiveModel(request)
            : request.PreMatch is not null
                ? EstimateFromPreMatchModel(request)
                : EstimateFromManualInput(request);

        var liveCaveat = diagnostics is null ? null : PaceModel.CaveatFor(diagnostics.Pace);

        // The bookmaker's own de-vigged line, always computed: it is what the edge is measured
        // against when there is no reference, and the thing the reference is compared to when there is.
        var bookFairProbability = OddsMath.DeVig(market.DecimalOdds, market.SiblingOdds);
        var reference = BuildReference(request, bookFairProbability);
        var fairProbability = reference?.FairProbability ?? bookFairProbability;

        var edge = new EdgeResult(
            fairProbability,
            estimate.OwnProbability,
            (estimate.OwnProbability - fairProbability) * 100.0,
            OddsMath.ExpectedValuePerUnit(estimate.OwnProbability, market.DecimalOdds));

        var verdict = VerdictEngine.Decide(edge, isLive);

        // A SKIP is a hard stop on sizing, so it short-circuits Kelly entirely and says so. Anything
        // else goes through Kelly, which returns its own zero stake if the price does not cover the
        // estimate — an edge over the fair line is not the same as an edge over the price offered.
        var stake = verdict == Verdict.Skip
            ? KellyStaking.NoStake("skip-below-threshold")
            : KellyStaking.RecommendStake(estimate.OwnProbability, market.DecimalOdds, request.Bankroll);

        // Advice on top of the verdict, never in place of it: the verdict and the stake stay what
        // they would have been, and the reader sees both.
        var timing = isLive && diagnostics is not null
            ? TimingAdvisor.Advise(
                market.Kind,
                ResolveSelection(request),
                request.LiveState!,
                request.Pace ?? PaceSettings.Default,
                diagnostics.Pace,
                edge,
                verdict,
                request.FeedAgeSeconds)
            : null;

        return new AnalyzeResponse(
            edge,
            stake,
            verdict,
            VerdictEngine.Explain(edge, verdict, isLive, stake, liveCaveat),
            estimate,
            OddsMath.Overround(market.DecimalOdds, market.SiblingOdds),
            diagnostics,
            Disclaimer,
            reference,
            timing);
    }

    /// <summary>
    /// Prices the same outcome off the reference book, when one was supplied.
    /// </summary>
    /// <remarks>
    /// Only the fair probability moves. EV and the Kelly stake stay on <c>Market.DecimalOdds</c>
    /// deliberately: the reference says how likely the outcome is, it does not say what you will be
    /// paid. Sizing against a price nobody will give you is the one way this feature could lose
    /// money, so the two numbers never swap places.
    /// </remarks>
    private static ReferenceLine? BuildReference(AnalyzeRequest request, double bookFairProbability)
    {
        if (request.ReferenceMarket is not { } reference)
        {
            return null;
        }

        var fair = OddsMath.DeVig(reference.DecimalOdds, reference.SiblingOdds);

        return new ReferenceLine(
            string.IsNullOrWhiteSpace(request.ReferenceSource) ? "reference book" : request.ReferenceSource,
            fair,
            OddsMath.Overround(reference.DecimalOdds, reference.SiblingOdds),
            bookFairProbability,
            (fair - bookFairProbability) * 100.0);
    }

    private static (ProbabilityEstimate, ModelDiagnostics) EstimateFromLiveModel(AnalyzeRequest request)
        => PriceLive(request.Market.Kind, ResolveSelection(request), request.LiveState!, request.Pace, request.PreMatch);

    /// <summary>
    /// The live model on its own: how likely this outcome is, given where the match is now.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="Analyze"/> because the same question gets asked twice about the
    /// same bet, once before it is placed and again while it runs. Re-pricing an open position is
    /// not a different calculation, and it must not be allowed to become one — a second
    /// implementation that drifted from this one would quietly disagree with the row it was
    /// explaining.
    /// </remarks>
    /// <param name="preMatch">This match's pre-match expected goals, when the user worked them out. They
    /// become the target the pace is shrunk towards in place of the league average, so what the app
    /// knows about these two teams is not thrown away the moment the match kicks off.</param>
    public static (ProbabilityEstimate Estimate, ModelDiagnostics Diagnostics) PriceLive(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceSettings? pace,
        PreMatchRates? preMatch = null)
    {
        if (state.IsPreMatch)
        {
            throw new AnalysisInputException(
                "The live model needs a match in progress: minute played and the xG accrued so far.");
        }

        if (preMatch is not null)
        {
            ValidatePreMatch(preMatch);
        }

        // Pace correction is the default, not an opt-in. The raw extrapolation is only right for a
        // match running at an unremarkable tempo, and the cases where it is most wrong are exactly
        // the ones that look most like a bet.
        var paceSettings = pace ?? PaceSettings.Default;

        PaceAdjustment adjustment;
        GoalDistribution distribution;
        try
        {
            adjustment = PaceModel.Adjust(state, paceSettings, preMatch);
            distribution = PoissonGoalModel.ExtrapolateFrom(state, adjustment);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new AnalysisInputException(ex.Message);
        }

        MarketProbability resolved;
        try
        {
            resolved = MarketProbabilityResolver.Resolve(kind, selection, distribution);
        }
        catch (ArgumentException ex)
        {
            throw new AnalysisInputException(ex.Message);
        }

        var notes = new List<string> { resolved.Description };
        notes.AddRange(adjustment.Steps);
        if (adjustment.Warning is not null)
        {
            notes.Add(adjustment.Warning);
        }
        notes.Add(PaceModel.CaveatFor(adjustment));

        var estimate = new ProbabilityEstimate(
            resolved.Probability,
            adjustment.PriorFromPreMatch ? LivePreMatchPriorMethod
            : adjustment.IsAdjusted ? "poisson-live-xg-paced"
            : "poisson-live-xg",
            string.Join(" ", notes));

        var diagnostics = new ModelDiagnostics(
            distribution.RemainingMinutes,
            adjustment.RawRateHome,
            adjustment.RawRateAway,
            distribution.ExpectedRemainingHome,
            distribution.ExpectedRemainingAway,
            resolved.RawWinProbability,
            resolved.PushProbability,
            Describe(selection),
            adjustment);

        return (estimate, diagnostics);
    }

    /// <summary>
    /// Prices the outcome off the pre-match model's expected goals, so a match that has not started
    /// gets a probability from the teams and their players instead of from a guess.
    /// </summary>
    private static (ProbabilityEstimate, ModelDiagnostics?) EstimateFromPreMatchModel(AnalyzeRequest request)
    {
        var rates = request.PreMatch!;
        ValidatePreMatch(rates);

        var selection = ResolveSelection(request);

        MarketProbability resolved;
        try
        {
            resolved = MarketProbabilityResolver.Resolve(
                request.Market.Kind,
                selection,
                PoissonGoalModel.FullMatch(rates.HomeExpectedGoals, rates.AwayExpectedGoals));
        }
        catch (ArgumentException ex)
        {
            throw new AnalysisInputException(ex.Message);
        }

        return (
            new ProbabilityEstimate(
                resolved.Probability,
                PreMatchMethod,
                resolved.Description +
                " Expected goals come from the teams' season form and the players expected to play; " +
                "they do not know about tactics, motivation or news that broke after the data was read."),
            null);
    }

    /// <summary>The method code stored with the estimate, and translated on screen.</summary>
    public const string PreMatchMethod = "poisson-prematch-squad";

    /// <summary>
    /// The live model with its pace shrunk towards the pre-match numbers rather than the league
    /// average. A separate code so the positions log can tell the two apart when they are judged on
    /// closing-line value later.
    /// </summary>
    public const string LivePreMatchPriorMethod = "poisson-live-xg-paced-prematch";

    private static void ValidatePreMatch(PreMatchRates rates)
    {
        if (!(rates.HomeExpectedGoals > 0.0) || !(rates.AwayExpectedGoals > 0.0)
            || double.IsInfinity(rates.HomeExpectedGoals) || double.IsInfinity(rates.AwayExpectedGoals))
        {
            throw new AnalysisInputException(
                "The pre-match expected goals must be positive numbers for both teams.");
        }
    }

    private static (ProbabilityEstimate, ModelDiagnostics?) EstimateFromManualInput(AnalyzeRequest request)
    {
        var own = request.ManualOwnProbability
                  ?? throw new AnalysisInputException(
                      "Pre-match analysis has no live xG to work from, so it needs your own probability " +
                      "estimate (0..1) in manualOwnProbability.");

        if (own is < 0.0 or > 1.0)
        {
            throw new AnalysisInputException($"manualOwnProbability must be between 0 and 1, got {own}.");
        }

        var estimate = new ProbabilityEstimate(
            own,
            "manual-input",
            "Entered by hand — no model ran. The edge below is only as good as this number, and nothing " +
            "here validates it.");

        return (estimate, null);
    }

    private static MarketSelectionSpec ResolveSelection(AnalyzeRequest request)
    {
        // An explicit selection always wins; the parser only covers the hand-typed-request case.
        if (request.Selection is { } side)
        {
            return new MarketSelectionSpec(side, request.Line);
        }

        MarketSelectionSpec parsed;
        try
        {
            parsed = MarketSelectionParser.Parse(request.Market.Kind, request.Market.Label);
        }
        catch (ArgumentException ex)
        {
            throw new AnalysisInputException(ex.Message);
        }

        return request.Line is { } line ? parsed with { Line = line } : parsed;
    }

    // Invariant, not current-culture: this string is part of the API response and gets stored in the
    // positions log, so it must not turn into "Over 2,5" on a machine with a comma decimal separator.
    private static string Describe(MarketSelectionSpec selection)
        => selection.Line is { } line
            ? $"{selection.Side} {line.ToString("0.##", CultureInfo.InvariantCulture)}"
            : selection.Side.ToString();

    private static void Validate(AnalyzeRequest request)
    {
        if (request.Market is null)
        {
            throw new AnalysisInputException("A market is required.");
        }

        if (request.Market.DecimalOdds <= 1.0)
        {
            throw new AnalysisInputException(
                $"Decimal odds must be greater than 1.0, got {request.Market.DecimalOdds}.");
        }

        if (request.Market.SiblingOdds is null || request.Market.SiblingOdds.Length == 0)
        {
            throw new AnalysisInputException(
                "At least one sibling price is required: without the other side of the market there is " +
                "no way to tell how much margin the bookmaker built into this one.");
        }

        if (request.Market.SiblingOdds.Any(o => o <= 1.0))
        {
            throw new AnalysisInputException("Every sibling price must also be greater than 1.0.");
        }

        if (request.Bankroll < 0.0 || double.IsNaN(request.Bankroll) || double.IsInfinity(request.Bankroll))
        {
            throw new AnalysisInputException($"Bankroll must be a non-negative number, got {request.Bankroll}.");
        }

        if (request.ReferenceMarket is { } reference)
        {
            if (reference.DecimalOdds <= 1.0)
            {
                throw new AnalysisInputException(
                    $"The reference price must be greater than 1.0, got {reference.DecimalOdds}.");
            }

            if (reference.SiblingOdds is null || reference.SiblingOdds.Length == 0)
            {
                throw new AnalysisInputException(
                    "The reference market needs its other outcomes too: a single price there carries no " +
                    "more information about a fair line than a single price here does.");
            }

            if (reference.SiblingOdds.Any(o => o <= 1.0))
            {
                throw new AnalysisInputException("Every reference price must also be greater than 1.0.");
            }
        }
    }
}
