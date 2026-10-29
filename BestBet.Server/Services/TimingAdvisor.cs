using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Says whether a live bet the model likes should be taken now or looked at again in a few minutes.
/// </summary>
/// <remarks>
/// The verdict answers "is there an edge at this price?". This answers a different question: "is the
/// number the edge was measured with trustworthy right now?". The two come apart in a few ways that
/// are visible in the data, and every one of them points the same way — the model sees less of the
/// match than the market does, and the gap shows up as an edge.
///
///   • A GAP TOO BIG TO BE TRUE. A live model that finds 30 points against a liquid market has not
///     found 30 points. The market has seen something the feed has not — a goal still being drawn
///     into the xG, a penalty, a red card — or it knows these two teams score more than an average
///     side. Either way the "edge" is the model's error, and the bigger it is the surer that is.
///   • TOO EARLY, ON A LEAGUE AVERAGE. Before half an hour most of the number is the prior, and when
///     no pre-match numbers were given that prior is a mid-table match. The market knows who is
///     playing; an Under on PSG–Barcelona priced off "an average match" is a bet on the average.
///   • A HOT START UNDER AN OVER. A pace no match keeps up is the case the shrinkage exists for, and
///     how far it pulls is a judgement, not a measurement. A bet that needs that pace to hold is
///     betting on the judgement; ten minutes show whether the pace is holding.
///   • THE MATCH IS TURNING AGAINST THE BET. The rate blends the last twenty minutes with the whole
///     match, so a game that has just opened up is only half in it. For a bet that loses on goals
///     that half is the dangerous one; the market reads it in full.
///   • OLD DATA. The price on the screen is live; a reading a minute old may be missing the event
///     that moved it.
///
/// It only advises. It never changes the verdict or the stake, so "BET" still means the same thing
/// it always did and the reader can see both answers side by side.
/// </remarks>
public static class TimingAdvisor
{
    /// <summary>Edge in points past which a live disagreement is more likely the model's error than value.</summary>
    /// <remarks>Twice what a live bet needs. The live edges that hold up are a few points, not tens.</remarks>
    public const double SuspiciousEdgePp = 2 * VerdictEngine.LiveEdgeThresholdPp;

    /// <summary>
    /// Share of the estimate that has to come from the match itself before a league-average prior
    /// stops being the main thing priced. 0.4 is reached at minute 30.
    /// </summary>
    public const double EnoughEvidence = 0.4;

    /// <summary>With no prior at all, the minutes a raw rate needs before it is more than a few shots.</summary>
    public const double EarlyMinutes = 30.0;

    /// <summary>How much faster (or slower) the recent window has to run than the match for it to count as a change.</summary>
    public const double MomentumRatio = 1.5;

    /// <summary>
    /// Below this, both sides together per ninety minutes, a "heating up" window is still a quiet
    /// one — 0.2 xG in twenty minutes doubling a dead first hour is not a game opening up. About an
    /// average match.
    /// </summary>
    public const double LivelyPer90 = 2.5;

    /// <summary>Seconds after which a reading is too old to bet against a live price.</summary>
    /// <remarks>
    /// Shorter than the client's own staleness warning, which is about whether a reading may drive
    /// the form at all. This one is about money: a minute is long enough for a goal and its replay.
    /// </remarks>
    public const double StaleAfterSeconds = 60.0;

    public const string EdgeTooLarge = "edge-too-large";
    public const string HotStart = "hot-start";
    public const string EarlyLeaguePrior = "early-league-prior";
    public const string EarlySmallSample = "early-small-sample";
    public const string HeatingUp = "heating-up";
    public const string CoolingDown = "cooling-down";
    public const string StaleData = "stale-data";

    /// <summary>Range searched when reading the market's goals back out of its price, as a multiple of the model's.</summary>
    private const double MinScale = 0.02;
    private const double MaxScale = 10.0;

    /// <summary>
    /// The advice for one live analysis. Null when there is nothing to time: a pre-match analysis,
    /// or a verdict of SKIP, which is already "do not bet".
    /// </summary>
    /// <param name="feedAgeSeconds">How old the reading in the form was when the analysis was asked
    /// for. Null when the numbers were typed in, which says nothing about their age.</param>
    public static TimingAdvice? Advise(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceSettings settings,
        PaceAdjustment adjustment,
        EdgeResult edge,
        Verdict verdict,
        double? feedAgeSeconds)
    {
        if (state.IsPreMatch || verdict == Verdict.Skip)
        {
            return null;
        }

        var reasons = new List<TimingReason>();
        var leaguePrior = settings.Shrink && !adjustment.PriorFromPreMatch;

        if (edge.EdgePercentagePoints >= SuspiciousEdgePp)
        {
            reasons.Add(new TimingReason(EdgeTooLarge, edge.EdgePercentagePoints, SuspiciousEdgePp));
        }

        if (feedAgeSeconds is { } age && age >= StaleAfterSeconds)
        {
            reasons.Add(new TimingReason(StaleData, Math.Round(age), StaleAfterSeconds));
        }

        if (adjustment.Warning is not null && GoalDirection(kind, selection.Side) > 0)
        {
            reasons.Add(new TimingReason(
                HotStart,
                Math.Max(adjustment.ProjectedFullMatchXgHome, adjustment.ProjectedFullMatchXgAway),
                PaceModel.ImplausibleProjectedXg));
        }

        if (leaguePrior && adjustment.EvidenceWeight < EnoughEvidence)
        {
            reasons.Add(new TimingReason(EarlyLeaguePrior, adjustment.EvidenceWeight, MinuteWithEnoughEvidence(settings)));
        }
        else if (!settings.Shrink && adjustment.ElapsedMinutes < EarlyMinutes)
        {
            reasons.Add(new TimingReason(EarlySmallSample, adjustment.ElapsedMinutes, EarlyMinutes));
        }

        if (Momentum(kind, selection, settings, adjustment) is { } momentum)
        {
            reasons.Add(momentum);
        }

        return new TimingAdvice(
            reasons.Count == 0 ? TimingAction.BetNow : TimingAction.Wait,
            reasons,
            adjustment.AdjustedRateTotal * adjustment.RemainingMinutes,
            MarketRemainingGoals(kind, selection, state, adjustment, edge.FairMarketProbability),
            leaguePrior);
    }

    /// <summary>
    /// Which way goals move this bet: −1 when more goals hurt it, +1 when they help, 0 when it
    /// depends on who scores them.
    /// </summary>
    public static int GoalDirection(MarketKind kind, MarketSelection side) => (kind, side) switch
    {
        (MarketKind.TotalGoals, MarketSelection.Under) => -1,
        (MarketKind.TotalGoals, MarketSelection.Over) => 1,
        (MarketKind.BothTeamsToScore, MarketSelection.No) => -1,
        (MarketKind.BothTeamsToScore, MarketSelection.Yes) => 1,
        (MarketKind.NextGoal, MarketSelection.NextGoalNone) => -1,
        _ => 0,
    };

    /// <summary>The minute at which a league-average prior stops outweighing the match, to come back at.</summary>
    /// <remarks>
    /// The evidence is counted in average minutes when the tempo is, and the opening of a match is
    /// worth fewer of those than its length — so the minute on the clock comes a little later.
    /// </remarks>
    private static double MinuteWithEnoughEvidence(PaceSettings settings)
    {
        var needed = EnoughEvidence * PaceModel.PriorStrengthMinutes / (1.0 - EnoughEvidence);
        var matchMinutes = settings.Stoppage ? MatchClock.TypicalMatchMinutes : PoissonGoalModel.RegulationMinutes;

        return Math.Ceiling(settings.TimeProfile ? MatchTempo.MinuteAt(needed, matchMinutes) : needed);
    }

    /// <summary>
    /// A recent window that runs against the bet: a match opening up under an Under, or dying under
    /// an Over.
    /// </summary>
    /// <remarks>
    /// Compared on the raw rates, the ones a reader can check against the provider's own numbers.
    /// The last twenty minutes of a match run a little hotter than its average anyway, which is
    /// why the ratio asked for is well past that drift.
    /// </remarks>
    private static TimingReason? Momentum(
        MarketKind kind,
        MarketSelectionSpec selection,
        PaceSettings settings,
        PaceAdjustment adjustment)
    {
        var direction = GoalDirection(kind, selection.Side);
        if (direction == 0 || !settings.HasRecentWindow)
        {
            return null;
        }

        var recent = (settings.RecentXgHome!.Value + settings.RecentXgAway!.Value)
                     / settings.RecentWindowMinutes!.Value * PoissonGoalModel.RegulationMinutes;
        var whole = adjustment.RawRateTotal * PoissonGoalModel.RegulationMinutes;

        if (direction < 0 && recent >= MomentumRatio * whole && recent >= LivelyPer90)
        {
            return new TimingReason(HeatingUp, recent, whole);
        }

        if (direction > 0 && recent * MomentumRatio <= whole)
        {
            return new TimingReason(CoolingDown, recent, whole);
        }

        return null;
    }

    /// <summary>
    /// How many more goals the market is pricing in, found as the number that would make the model
    /// quote the market's own fair probability for this line.
    /// </summary>
    /// <remarks>
    /// Both sides' rates are scaled together, so the answer keeps the model's split between them
    /// and only asks "how many?". Totals only: it is the one market where more goals always move the
    /// price the same way, which is what lets a single number be read back out of it.
    /// </remarks>
    public static double? MarketRemainingGoals(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceAdjustment adjustment,
        double fairProbability)
    {
        if (kind != MarketKind.TotalGoals)
        {
            return null;
        }

        var modelGoals = adjustment.AdjustedRateTotal * adjustment.RemainingMinutes;
        if (!(modelGoals > 0.0))
        {
            return null;
        }

        double Price(double scale)
            => MarketProbabilityResolver.Resolve(
                kind,
                selection,
                PoissonGoalModel.ExtrapolateFrom(
                    state,
                    adjustment with
                    {
                        AdjustedRateHome = adjustment.AdjustedRateHome * scale,
                        AdjustedRateAway = adjustment.AdjustedRateAway * scale,
                    })).Probability;

        var low = MinScale;
        var high = MaxScale;
        var atLow = Price(low);
        var atHigh = Price(high);

        // A line already decided, or a market priced beyond anything a match produces.
        if (fairProbability < Math.Min(atLow, atHigh) || fairProbability > Math.Max(atLow, atHigh)
            || Math.Abs(atHigh - atLow) < 1e-9)
        {
            return null;
        }

        var rising = atHigh > atLow;
        for (var i = 0; i < 60; i++)
        {
            var middle = Math.Sqrt(low * high);
            var below = Price(middle) < fairProbability;
            if (below == rising)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return Math.Sqrt(low * high) * modelGoals;
    }
}
