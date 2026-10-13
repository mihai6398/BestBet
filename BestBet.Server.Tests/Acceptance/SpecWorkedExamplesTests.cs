using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Acceptance;

/// <summary>
/// The worked examples from BESTBET_TASK.md, pinned as tests. Section 9 makes these the definition of
/// "v1 is done": if any of them drifts, the de-vig or the Poisson model has a bug and that gets fixed
/// before anything else is built on top.
///
/// Every live case here passes <see cref="PaceSettings.Flat"/> explicitly. The app now corrects the
/// extrapolated pace by default — see <see cref="PaceModel"/> and MatchDynamicsTests — but the spec's
/// arithmetic is what these tests exist to protect, so they ask for it by name rather than inheriting
/// whatever the default happens to be.
/// </summary>
public class SpecWorkedExamplesTests
{
    private readonly AnalysisService _service = new();

    /// <summary>
    /// Spec section 4.2. A two-way book of 1.57 / 1.66 carries a 1.2394 overround and de-vigs the
    /// 1.57 side to 51.4%.
    /// </summary>
    [Fact]
    public void Section_4_2_two_way_de_vig()
    {
        Assert.Equal(0.6369, OddsMath.ImpliedProbability(1.57), 4);
        Assert.Equal(0.6024, OddsMath.ImpliedProbability(1.66), 4);
        Assert.Equal(1.2394, OddsMath.Overround(1.57, [1.66]), 4);
        Assert.Equal(0.5139, OddsMath.DeVig(1.57, [1.66]), 4);
    }

    /// <summary>
    /// Spec section 5, the request half of the API example: "Sub 4" at 1.57 with 1.66 on the other
    /// side, at minute 67 of a 1-0 game with 0.79 / 0.01 accumulated xG.
    /// </summary>
    /// <remarks>
    /// The RESPONSE printed in section 5 is illustrative, not derived — its own-probability of 0.60
    /// does not come from the section 4.3 model, and its verdict and cap-reason do not follow from the
    /// section 4.4 and 4.5 rules for the numbers it shows. Section 4 is the normative part ("exact
    /// rules, not vibes"), so this test pins what those rules actually produce.
    ///
    /// What they produce: 0.79 xG over 67 minutes is 0.0118/min, which over the 23 remaining minutes
    /// is 0.271 more goals; the away side adds 0.003. One goal is already scored, so finishing under
    /// four is all but certain — 99.7%, with a 0.26% chance of landing exactly on four and pushing.
    /// The market's 51.4% is therefore nowhere near it, and the bet is a clear BET rather than the
    /// MARGINAL the illustrative JSON shows.
    /// </remarks>
    [Fact]
    public void Section_5_api_example_inputs()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.57, [1.66]),
            new LiveMatchState(67, 0.79, 0.01, 1, 0, IsPreMatch: false),
            Bankroll: 263.0,
            Pace: PaceSettings.Flat));

        // The half of the example that IS derived: the de-vig.
        Assert.Equal(0.5139, response.Edge.FairMarketProbability, 4);

        // The model's own view of "Sub 4" from the live xG.
        Assert.Equal(23, response.Model!.RemainingMinutes);
        Assert.Equal(0.2712, response.Model.ExpectedRemainingHome, 4);
        Assert.Equal(0.0034, response.Model.ExpectedRemainingAway, 4);
        Assert.Equal(0.9972, response.Model.RawWinProbability, 4);
        Assert.Equal(0.0026, response.Model.PushProbability, 4);
        Assert.Equal(0.9998, response.Edge.OwnProbability, 4);

        // Which makes this a long way past the live threshold, with positive EV at the price offered.
        Assert.Equal(48.6, response.Edge.EdgePercentagePoints, 1);
        Assert.Equal(0.5697, response.Edge.ExpectedValuePerUnit, 4);
        Assert.Equal(Verdict.Bet, response.Verdict);
        Assert.Equal("cap-5pct", response.Stake.CapReason);
        Assert.Equal(13.15, response.Stake.SuggestedStakeAmount, 2);
    }

    /// <summary>
    /// Spec section 9, the acceptance case: Sunderland - Arsenal at minute 45, 0.13 home xG against
    /// 0.86 away, "Primul gol: 2" offered at 1.60, bankroll 263.
    /// </summary>
    /// <remarks>
    /// The model's own probability depends only on the match state, not on the book, so it is pinned
    /// exactly here: 45 minutes played and 45 to come means the remaining rates equal the xG already
    /// accumulated, giving a combined 0.99 expected goals. P(no further goal) is e^-0.99 = 37.2%, and
    /// the remaining 62.8% splits by scoring rate into 8.3% home and 54.6% away.
    ///
    /// 54.6% against a price of 1.60 is the whole story: 1.60 needs 62.5% to break even before any
    /// margin is removed, so the bet loses 12.7% per unit however the rest of the book is priced.
    /// </remarks>
    [Fact]
    public void Section_9_acceptance_the_model_half_which_does_not_depend_on_the_book()
    {
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(
            new LiveMatchState(45, 0.13, 0.86, 0, 0, IsPreMatch: false));

        Assert.Equal(45, distribution.RemainingMinutes);
        Assert.Equal(0.13, distribution.ExpectedRemainingHome, 10);
        Assert.Equal(0.86, distribution.ExpectedRemainingAway, 10);

        var awayNext = MarketProbabilityResolver.Resolve(
            MarketKind.NextGoal, new MarketSelectionSpec(MarketSelection.NextGoalAway, null), distribution);

        Assert.Equal(0.5459, awayNext.Probability, 4);
        Assert.Equal(-0.1266, OddsMath.ExpectedValuePerUnit(awayNext.Probability, 1.60), 4);

        // Kelly is negative, so no version of this bet gets staked.
        Assert.True(KellyStaking.FullKelly(awayNext.Probability, 1.60) < 0);
    }

    /// <summary>
    /// The same case end to end. The spec did not record the other two prices in that next-goal
    /// market, so a plausible book is stated here explicitly rather than smuggled in: 1.60 away,
    /// 3.20 home, 4.50 no-further-goal, a 16% overround.
    /// </summary>
    [Fact]
    public void Section_9_acceptance_end_to_end()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.NextGoal, "Primul gol: 2", 1.60, [3.20, 4.50]),
            new LiveMatchState(45, 0.13, 0.86, 0, 0, IsPreMatch: false),
            Bankroll: 263.0,
            Selection: MarketSelection.NextGoalAway,
            Pace: PaceSettings.Flat));

        Assert.Equal(0.5459, response.Edge.OwnProbability, 4);
        Assert.Equal(0.5389, response.Edge.FairMarketProbability, 4);
        Assert.Equal(0.70, response.Edge.EdgePercentagePoints, 2);
        Assert.Equal(-0.1266, response.Edge.ExpectedValuePerUnit, 4);

        // 0.7pp is nowhere near the 5pp a live market has to clear, nor the 3pp marginal floor.
        Assert.Equal(Verdict.Skip, response.Verdict);
        Assert.Equal(0.0, response.Stake.SuggestedStakeAmount);
        Assert.Equal("skip-below-threshold", response.Stake.CapReason);
        Assert.Contains(PoissonGoalModel.PaceCaveat, response.VerdictReason);
        Assert.Equal("poisson-live-xg", response.Estimate.Method);
    }

    /// <summary>
    /// The verdict in the section 9 case must not flip on the exact book used. Whatever the other two
    /// prices are, 1.60 on a 54.6% shot cannot be a bet, because the price alone is against it.
    /// </summary>
    [Theory]
    [InlineData(3.00, 4.00)]
    [InlineData(3.20, 4.50)]
    [InlineData(3.60, 6.00)]
    [InlineData(4.00, 9.00)]
    public void Section_9_stays_a_skip_across_any_plausible_book(double homeOdds, double noGoalOdds)
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.NextGoal, "Primul gol: 2", 1.60, [homeOdds, noGoalOdds]),
            new LiveMatchState(45, 0.13, 0.86, 0, 0, IsPreMatch: false),
            Bankroll: 263.0,
            Selection: MarketSelection.NextGoalAway));

        Assert.Equal(Verdict.Skip, response.Verdict);
        Assert.Equal(0.0, response.Stake.SuggestedStakeAmount);
        Assert.True(response.Edge.ExpectedValuePerUnit < 0);
    }

    /// <summary>
    /// The case the spec warns about in 4.3: a match sitting scoreless for a long stretch despite
    /// large accumulated xG. The model reads the average rate off the clock and keeps extrapolating
    /// it, which is exactly why its output is documented as a floor.
    /// </summary>
    [Fact]
    public void The_quiet_match_case_the_model_is_known_to_under_price()
    {
        // 0-0 at minute 76 with 1.9 xG between the two sides. Averaged over 76 minutes that is a
        // modest rate, so the model expects only 0.35 more goals in the 14 minutes left and puts
        // P(no further goal) at 70.5% — while a watcher who has just seen 1.9 xG go unconverted
        // would price a late goal a good deal shorter than 29.5%.
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(
            new LiveMatchState(76, 1.20, 0.70, 0, 0, IsPreMatch: false));

        var noMoreGoals = MarketProbabilityResolver.Resolve(
            MarketKind.NextGoal, new MarketSelectionSpec(MarketSelection.NextGoalNone, null), distribution);

        Assert.Equal(0.35, distribution.ExpectedRemainingHome + distribution.ExpectedRemainingAway, 10);
        Assert.Equal(0.7047, noMoreGoals.Probability, 4);

        // The caveat is not optional garnish — it is part of what the model returns.
        Assert.Contains("floor", PoissonGoalModel.PaceCaveat);
    }
}
