using BestBet.Contracts;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Plans when to take a live price: now, or a few minutes of play from now.
/// </summary>
/// <remarks>
/// The verdict answers "is there an edge at this price?". This answers "is now the best moment to
/// take it?" — and the honest answer to that depends on a question the verdict never asks: when the
/// model and the market disagree, which of them is right?
///
/// THE TWO VIEWS. The model's estimate is part evidence from this match and part prior — a league
/// average, or the pre-match numbers. The prior is something the market knows at least as well; the
/// only thing the model can know better is what this match has shown. So the model is believed over
/// the market by exactly that share, its CREDIBILITY: about 30% at minute 23, over 60% by the last
/// quarter of an hour, and half that when the reading is a minute old. Every probability the plan
/// acts on is the market's, moved towards the model's by that much.
///
/// THE NEXT FEW MINUTES. For each later moment — 5, 10 and 15 minutes of play from now — the plan
/// lays out what can happen in between: no goal, a goal for either side, more. Each is weighed under
/// both views, because they disagree about how likely it is; that disagreement is the point. Then, for
/// each, it moves the match on — the clock, the score, the xG a scenario with goals in it would have
/// produced — and asks again what the model says, what the market would say, and what the bookmaker
/// would most likely be paying.
///
/// WHAT WAITING LEARNS. The goals that do or do not come are evidence about which view was right:
/// a model expecting fewer goals than the market gains credibility with every quiet minute and loses
/// it with every goal, by Bayes' rule on how likely each view thought that was. And the minutes
/// themselves add to the match's share of the estimate. Waiting buys both — at the cost of the price
/// moving, and of the scenarios in which there is nothing left worth betting on.
///
/// THE DECISION. Each moment is valued as "wait until then, and bet only if it is still worth it,
/// at the stake Kelly would size it at": the expected profit over everything that can happen. Now is
/// valued the same way with nothing in between. The plan waits when a later moment is clearly worth
/// more, skips when no moment is worth anything, and otherwise takes the price that is there.
///
/// On top of that sit the warnings: a gap too big to be true, a reading too old, a hot start under an
/// Over, a match turning against the bet. Any one of them stops a "bet now" — they are things the plan
/// cannot price but the reader should not bet through.
///
/// It only advises. It never changes the verdict or the stake, so "BET" still means what it always
/// did and both answers are on the screen side by side.
///
/// Everything here is a heuristic with its reasoning stated, like the pace model it sits on. The
/// scenarios are priced by that same model, so they are exactly as good as it is.
/// </remarks>
public static class TimingAdvisor
{
    // --- Warnings -------------------------------------------------------------

    /// <summary>Edge in points past which a live disagreement is more likely the model's error than value.</summary>
    /// <remarks>Twice what a live bet needs. The live edges that hold up are a few points, not tens.</remarks>
    public const double SuspiciousEdgePp = 2 * VerdictEngine.LiveEdgeThresholdPp;

    /// <summary>
    /// Share of the estimate that has to come from the match itself before a league-average prior
    /// stops being the main thing priced. 0.4 is reached at about minute 33.
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
    public const string StaleData = "stale-data";
    public const string HotStart = "hot-start";
    public const string EarlyLeaguePrior = "early-league-prior";
    public const string EarlySmallSample = "early-small-sample";
    public const string HeatingUp = "heating-up";
    public const string CoolingDown = "cooling-down";

    // --- The plan -------------------------------------------------------------

    /// <summary>The later moments looked at, in minutes of play from now.</summary>
    public static readonly IReadOnlyList<int> Horizons = [5, 10, 15];

    /// <summary>A later moment has to leave at least this much of the match to be worth planning for.</summary>
    public const double MinutesLeftToPlan = 5.0;

    /// <summary>
    /// Expected goals a goal brings with it. A goal is a chance that went in, and the chances that go
    /// in carry about a third of a goal of xG on average — so a scenario with a goal in it is one with
    /// more xG on the clock too, and the model sees that, not just the score.
    /// </summary>
    /// <remarks>
    /// The rest of the window's xG is spread so the expectation over every scenario is exactly what
    /// the model expects to accrue: the scenarios redistribute the xG, they do not invent any.
    /// </remarks>
    public const double XgPerGoal = 0.35;

    /// <summary>A reading older than <see cref="StaleAfterSeconds"/> is believed half as much.</summary>
    public const double StaleCredibilityFactor = 0.5;

    /// <summary>
    /// Expected profit below this share of the bankroll is noise, not value: 0.05%, a third of what a
    /// minimum stake at a healthy edge returns.
    /// </summary>
    public const double MinimumValue = 0.0005;

    /// <summary>
    /// How much more a later moment has to be worth before giving up the price on the screen for it.
    /// The price may not be there, the market may be suspended, the reader may miss the minute — a
    /// sure thing now is worth a little more than its expectation later.
    /// </summary>
    public const double WaitGain = 0.15;

    /// <summary>And by at least this much of the bankroll, so rounding never decides it: 0.01%.</summary>
    public const double MinimumGain = 0.0001;

    /// <summary>
    /// Past this fair probability — or short of one minus it — there is no price left worth taking:
    /// a bet all but won pays nothing, and one all but lost is a price nobody should be quoted.
    /// </summary>
    public const double MaxPrice = 0.97;

    /// <summary>Goals per side in a window counted one by one; anything more is counted as this many.</summary>
    private const int MaxWindowGoals = 3;

    /// <summary>Range searched when reading the market's goals back out of its price, as a multiple of the model's.</summary>
    private const double MinScale = 0.02;
    private const double MaxScale = 10.0;

    /// <summary>
    /// The plan for one live analysis. Null before kick-off, where there is no match to wait on.
    /// </summary>
    /// <param name="prior">The pre-match rates the analysis was shrunk towards, so the scenarios are
    /// priced on the same terms as the number they are compared with.</param>
    /// <param name="decimalOdds">The price on offer now.</param>
    /// <param name="feedAgeSeconds">How old the reading in the form was when the analysis was asked
    /// for. Null when the numbers were typed in, which says nothing about their age.</param>
    public static TimingAdvice? Advise(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceSettings settings,
        PreMatchRates? prior,
        PaceAdjustment adjustment,
        EdgeResult edge,
        double decimalOdds,
        double? feedAgeSeconds)
    {
        if (state.IsPreMatch)
        {
            return null;
        }

        var stale = feedAgeSeconds is { } age && age >= StaleAfterSeconds;
        var reasons = Warnings(kind, selection, settings, adjustment, edge, feedAgeSeconds, stale);

        var fair = edge.FairMarketProbability;
        var scale = MarketScale(kind, selection, state, adjustment, fair);
        var modelGoals = adjustment.AdjustedRateTotal * adjustment.RemainingMinutes;
        var credibility = Credibility(adjustment, settings) * (stale ? StaleCredibilityFactor : 1.0);

        var market = new MarketView(scale, Logit(fair) - Logit(edge.OwnProbability), fair, decimalOdds);
        var options = new List<TimingOption>
        {
            Now(state, edge.OwnProbability, fair, decimalOdds, credibility),
        };

        foreach (var minutes in Horizons)
        {
            if (adjustment.RemainingMinutes - minutes < MinutesLeftToPlan)
            {
                break;
            }

            options.Add(Later(kind, selection, state, settings, prior, adjustment, credibility, market, minutes));
        }

        var (action, wait) = Decide(options, reasons);

        return new TimingAdvice(
            action,
            wait,
            reasons,
            credibility,
            modelGoals,
            scale is { } s ? s * modelGoals : null,
            settings.Shrink && !adjustment.PriorFromPreMatch,
            options);
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

    /// <summary>
    /// How far the model is believed over the market: the share of its estimate that is evidence
    /// from this match, counted the way the shrinkage counts it.
    /// </summary>
    /// <remarks>
    /// Worked out from the minutes even when shrinkage is switched off. Turning the prior off does not
    /// make twenty minutes of football prove more than they did; it only stops the model saying so.
    /// </remarks>
    public static double Credibility(PaceAdjustment adjustment, PaceSettings settings)
    {
        var matchMinutes = settings.Stoppage ? MatchClock.TypicalMatchMinutes : PoissonGoalModel.RegulationMinutes;
        var evidence = settings.TimeProfile
            ? MatchTempo.Cumulative(adjustment.ElapsedMinutes, matchMinutes)
            : adjustment.ElapsedMinutes;

        return evidence / (evidence + PaceModel.PriorStrengthMinutes);
    }

    /// <summary>
    /// How many more goals the market is pricing in, as a multiple of the model's: the scale on both
    /// sides' rates that makes the model quote the market's own fair probability.
    /// </summary>
    /// <remarks>
    /// Both rates are scaled together, so the answer keeps the model's split between the sides and only
    /// asks "how many?". That only has an answer for a bet that more goals always move the same way —
    /// totals, both-teams-to-score, no further goal — and not for a line that is already settled.
    /// </remarks>
    public static double? MarketScale(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceAdjustment adjustment,
        double fairProbability)
    {
        if (GoalDirection(kind, selection.Side) == 0 || !(adjustment.AdjustedRateTotal > 0.0))
        {
            return null;
        }

        double Price(double scale) => Scaled(kind, selection, state, adjustment, scale);

        var low = MinScale;
        var high = MaxScale;
        var atLow = Price(low);
        var atHigh = Price(high);

        if (fairProbability < Math.Min(atLow, atHigh) || fairProbability > Math.Max(atLow, atHigh)
            || Math.Abs(atHigh - atLow) < 1e-9)
        {
            return null;
        }

        var rising = atHigh > atLow;
        for (var i = 0; i < 60; i++)
        {
            var middle = Math.Sqrt(low * high);
            if (Price(middle) < fairProbability == rising)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return Math.Sqrt(low * high);
    }

    // --- Warnings -------------------------------------------------------------

    private static List<TimingReason> Warnings(
        MarketKind kind,
        MarketSelectionSpec selection,
        PaceSettings settings,
        PaceAdjustment adjustment,
        EdgeResult edge,
        double? feedAgeSeconds,
        bool stale)
    {
        var reasons = new List<TimingReason>();

        if (edge.EdgePercentagePoints >= SuspiciousEdgePp)
        {
            reasons.Add(new TimingReason(EdgeTooLarge, edge.EdgePercentagePoints, SuspiciousEdgePp));
        }

        if (stale)
        {
            reasons.Add(new TimingReason(StaleData, Math.Round(feedAgeSeconds!.Value), StaleAfterSeconds));
        }

        if (adjustment.Warning is not null && GoalDirection(kind, selection.Side) > 0)
        {
            reasons.Add(new TimingReason(
                HotStart,
                Math.Max(adjustment.ProjectedFullMatchXgHome, adjustment.ProjectedFullMatchXgAway),
                PaceModel.ImplausibleProjectedXg));
        }

        if (settings.Shrink && !adjustment.PriorFromPreMatch && adjustment.EvidenceWeight < EnoughEvidence)
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

        return reasons;
    }

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

    // --- The plan -------------------------------------------------------------

    /// <summary>
    /// How the market is assumed to price the bet in a scenario: with its own number of goals when
    /// that could be read out of its price, and otherwise at the same distance from the model in
    /// log-odds as it is now.
    /// </summary>
    /// <remarks>
    /// Either way the market keeps its view rather than coming round to the model's. That is the
    /// cautious assumption for a plan that waits: if the market were going to agree with the model,
    /// the edge would be gone by the time the plan came back for it.
    /// </remarks>
    private sealed record MarketView(double? Scale, double LogitOffset, double FairNow, double OddsNow);

    private static TimingOption Now(
        LiveMatchState state,
        double ownProbability,
        double fair,
        double odds,
        double credibility)
    {
        var (fraction, value) = Value(credibility * ownProbability + (1 - credibility) * fair, odds);

        return new TimingOption(
            0,
            state.MinutePlayed,
            value,
            fraction > 0 ? 1.0 : 0.0,
            fraction > 0 ? odds : null,
            fraction,
            []);
    }

    private static TimingOption Later(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceSettings settings,
        PreMatchRates? prior,
        PaceAdjustment adjustment,
        double credibility,
        MarketView market,
        int minutes)
    {
        var scale = market.Scale ?? 1.0;
        var modelHome = Window(adjustment.AdjustedRateHome * minutes, adjustment.RateShape);
        var modelAway = Window(adjustment.AdjustedRateAway * minutes, adjustment.RateShape);
        var marketHome = Window(adjustment.AdjustedRateHome * minutes * scale, adjustment.RateShape);
        var marketAway = Window(adjustment.AdjustedRateAway * minutes * scale, adjustment.RateShape);

        // xG the window adds when nothing goes in; each goal brings XgPerGoal on top.
        var quietHome = adjustment.AdjustedRateHome * minutes * (1 - XgPerGoal);
        var quietAway = adjustment.AdjustedRateAway * minutes * (1 - XgPerGoal);

        var groups = new Dictionary<string, ScenarioSum>();
        double value = 0, betting = 0, oddsSum = 0, stakeSum = 0;
        var atMinute = state.MinutePlayed + minutes;

        for (var home = 0; home <= MaxWindowGoals; home++)
        {
            for (var away = 0; away <= MaxWindowGoals; away++)
            {
                var underModel = modelHome[home] * modelAway[away];
                var underMarket = marketHome[home] * marketAway[away];
                var weight = credibility * underModel + (1 - credibility) * underMarket;
                if (weight < 1e-9)
                {
                    continue;
                }

                var xgHome = quietHome + XgPerGoal * home;
                var xgAway = quietAway + XgPerGoal * away;
                var future = Advance(state, minutes, xgHome, xgAway, home, away);
                atMinute = future.MinutePlayed;

                var futureSettings = Shift(settings, minutes, xgHome, xgAway);
                var futureAdjustment = PaceModel.Adjust(future, futureSettings, prior);
                var model = Price(kind, selection, future, futureAdjustment);
                var fair = market.Scale is { } s
                    ? Scaled(kind, selection, future, futureAdjustment, s)
                    : Logistic(Logit(model) + market.LogitOffset);

                // The goals in between say which view was nearer, on top of what the extra minutes
                // add to the match's share of the estimate.
                var start = Credibility(futureAdjustment, futureSettings);
                var believed = start * underModel / (start * underModel + (1 - start) * underMarket);

                // The bookmaker's price moves with its fair probability; its margin stays as it is now.
                double? odds = fair >= MaxPrice || fair <= 1 - MaxPrice ? null : market.OddsNow * market.FairNow / fair;
                var (fraction, profit) = odds is { } price
                    ? Value(believed * model + (1 - believed) * fair, price)
                    : (0.0, 0.0);
                var bet = fraction > 0;

                value += weight * profit;
                if (bet)
                {
                    betting += weight;
                    oddsSum += weight * odds!.Value;
                    stakeSum += weight * fraction;
                }

                var code = (home, away) switch
                {
                    (0, 0) => "no-goal",
                    (1, 0) => "home-goal",
                    (0, 1) => "away-goal",
                    _ => "more-goals",
                };
                var group = groups.TryGetValue(code, out var sum) ? sum : new ScenarioSum();
                groups[code] = group.Add(weight, model, fair, odds, bet, believed);
            }
        }

        return new TimingOption(
            minutes,
            atMinute,
            value,
            betting,
            betting > 0 ? oddsSum / betting : null,
            betting > 0 ? stakeSum / betting : 0.0,
            new[] { "no-goal", "home-goal", "away-goal", "more-goals" }
                .Where(groups.ContainsKey)
                .Select(code => groups[code].ToScenario(code))
                .ToList());
    }

    /// <summary>
    /// Now, then the best later moment. No value anywhere is a skip; a later moment wins when now is
    /// not worth a bet or when it is clearly worth more; and a warning stops a "bet now" whatever the
    /// numbers say.
    /// </summary>
    /// <remarks>
    /// A warning only names a minute to come back at when one is actually better. Otherwise the wait
    /// is for the warning itself — the reading to refresh, the gap to explain itself — and inventing
    /// a minute for it would read as a forecast the plan never made.
    /// </remarks>
    private static (TimingAction Action, int? WaitMinutes) Decide(
        IReadOnlyList<TimingOption> options,
        IReadOnlyList<TimingReason> reasons)
    {
        var now = options[0];
        var later = options.Skip(1).Where(o => o.ExpectedValue >= MinimumValue).MaxBy(o => o.ExpectedValue);

        if (now.ExpectedValue < MinimumValue && later is null)
        {
            return (TimingAction.Skip, null);
        }

        var laterIsBetter = later is not null
            && (now.ExpectedValue < MinimumValue
                || later.ExpectedValue > now.ExpectedValue * (1 + WaitGain) + MinimumGain);

        if (laterIsBetter)
        {
            return (TimingAction.Wait, later!.MinutesFromNow);
        }

        return reasons.Count > 0 ? (TimingAction.Wait, null) : (TimingAction.BetNow, null);
    }

    /// <summary>The stake Kelly would size at this probability and price, and the profit it is expected to make.</summary>
    private static (double Fraction, double Profit) Value(double probability, double odds)
    {
        if (odds <= 1.0)
        {
            return (0.0, 0.0);
        }

        var fraction = KellyStaking.RecommendStake(probability, odds, 1.0).SuggestedStakeFraction;
        var perUnit = probability * odds - 1.0;

        return fraction > 0 && perUnit > 0 ? (fraction, fraction * perUnit) : (0.0, 0.0);
    }

    /// <summary>Goals in a window, 0..<see cref="MaxWindowGoals"/>, the last entry holding everything beyond.</summary>
    private static double[] Window(double mean, double? shape)
    {
        var table = shape is { } r
            ? PoissonGoalModel.NegativeBinomialPmfTable(mean, r, MaxWindowGoals)
            : PoissonGoalModel.PoissonPmfTable(mean, MaxWindowGoals);

        table[MaxWindowGoals] = Math.Max(0.0, 1.0 - table.Take(MaxWindowGoals).Sum());
        return table;
    }

    /// <summary>
    /// The match a few minutes of play from now: the clock moved on, across half-time if it gets
    /// there, with the scenario's goals and xG added.
    /// </summary>
    private static LiveMatchState Advance(
        LiveMatchState state,
        int minutes,
        double xgHome,
        double xgAway,
        int goalsHome,
        int goalsAway)
    {
        var period = MatchClock.PeriodOf(state);
        var firstHalfStoppage = MatchClock.FirstHalfStoppage(state);
        var elapsed = MatchClock.ElapsedMinutes(state) + minutes;

        var (minute, next) = period switch
        {
            MatchPeriod.SecondHalf => (state.MinutePlayed + minutes, MatchPeriod.SecondHalf),
            MatchPeriod.FirstHalf when elapsed <= MatchClock.HalfMinutes + firstHalfStoppage =>
                ((int)Math.Round(elapsed), MatchPeriod.FirstHalf),
            _ => (Math.Max(MatchClock.HalfMinutes + 1, (int)Math.Round(elapsed - firstHalfStoppage)), MatchPeriod.SecondHalf),
        };

        return state with
        {
            MinutePlayed = minute,
            Period = next,
            CumulativeXgHome = state.CumulativeXgHome + xgHome,
            CumulativeXgAway = state.CumulativeXgAway + xgAway,
            CurrentGoalsHome = state.CurrentGoalsHome + goalsHome,
            CurrentGoalsAway = state.CurrentGoalsAway + goalsAway,
        };
    }

    /// <summary>
    /// The recent window as it would read a few minutes on: the same length, the oldest part dropped
    /// and the scenario's xG added at the front.
    /// </summary>
    private static PaceSettings Shift(PaceSettings settings, int minutes, double xgHome, double xgAway)
    {
        if (!settings.HasRecentWindow)
        {
            return settings;
        }

        var window = settings.RecentWindowMinutes!.Value;
        var kept = Math.Max(0.0, (window - minutes) / window);
        var fresh = Math.Min(1.0, window / minutes);

        return settings with
        {
            RecentXgHome = settings.RecentXgHome!.Value * kept + xgHome * fresh,
            RecentXgAway = settings.RecentXgAway!.Value * kept + xgAway * fresh,
        };
    }

    private static double Price(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceAdjustment adjustment)
        => MarketProbabilityResolver.Resolve(kind, selection, PoissonGoalModel.ExtrapolateFrom(state, adjustment)).Probability;

    private static double Scaled(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state,
        PaceAdjustment adjustment,
        double scale)
        => Price(kind, selection, state, adjustment with
        {
            AdjustedRateHome = adjustment.AdjustedRateHome * scale,
            AdjustedRateAway = adjustment.AdjustedRateAway * scale,
        });

    private const double Edge = 1e-6;

    private static double Logit(double p)
    {
        var clamped = Math.Clamp(p, Edge, 1 - Edge);
        return Math.Log(clamped / (1 - clamped));
    }

    private static double Logistic(double x) => 1.0 / (1.0 + Math.Exp(-x));

    /// <summary>Running sums for one group of scenarios, weighted by how likely each is.</summary>
    private readonly record struct ScenarioSum(
        double Weight,
        double Model,
        double Market,
        double PricedWeight,
        double Odds,
        double Betting,
        double Credibility)
    {
        public ScenarioSum Add(double weight, double model, double market, double? odds, bool bet, double credibility)
            => new(
                Weight + weight,
                Model + weight * model,
                Market + weight * market,
                PricedWeight + (odds is null ? 0.0 : weight),
                Odds + (odds is { } price ? weight * price : 0.0),
                Betting + (bet ? weight : 0.0),
                Credibility + weight * credibility);

        public TimingScenario ToScenario(string code)
            => new(
                code,
                Weight,
                Model / Weight,
                Market / Weight,
                PricedWeight > 0 ? Odds / PricedWeight : null,
                Betting / Weight,
                Credibility / Weight);
    }
}
