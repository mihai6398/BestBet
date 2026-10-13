using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class VerdictEngineTests
{
    private static EdgeResult Edge(double edgePp, double ev = 0.05)
        => new(0.50, 0.50 + edgePp / 100.0, edgePp, ev);

    [Theory]
    [InlineData(12.0, Verdict.Bet)]
    [InlineData(8.6, Verdict.Bet)]
    [InlineData(5.0, Verdict.Bet)]      // exactly on the threshold counts as clearing it
    [InlineData(4.9, Verdict.Marginal)]
    [InlineData(3.0, Verdict.Marginal)] // bottom of the marginal band
    [InlineData(2.9, Verdict.Skip)]
    [InlineData(0.0, Verdict.Skip)]
    [InlineData(-4.0, Verdict.Skip)]
    public void Live_markets_have_to_clear_five_percentage_points(double edgePp, Verdict expected)
        => Assert.Equal(expected, VerdictEngine.Decide(Edge(edgePp), isLive: true));

    [Theory]
    [InlineData(5.0, Verdict.Bet)]
    [InlineData(3.0, Verdict.Bet)]      // exactly on the pre-match threshold
    [InlineData(2.9, Verdict.Marginal)]
    [InlineData(1.0, Verdict.Marginal)] // bottom of the marginal band
    [InlineData(0.9, Verdict.Skip)]
    [InlineData(-1.0, Verdict.Skip)]
    public void Pre_match_markets_only_have_to_clear_three(double edgePp, Verdict expected)
        => Assert.Equal(expected, VerdictEngine.Decide(Edge(edgePp), isLive: false));

    [Fact]
    public void The_live_bar_is_strictly_higher_than_the_pre_match_one()
    {
        // A 4pp edge is actionable before kick-off and not actionable in-play, because the live model
        // is cruder and the price moves under you.
        Assert.Equal(Verdict.Bet, VerdictEngine.Decide(Edge(4.0), isLive: false));
        Assert.Equal(Verdict.Marginal, VerdictEngine.Decide(Edge(4.0), isLive: true));
    }

    [Fact]
    public void Every_live_explanation_carries_the_pace_caveat()
    {
        var edge = Edge(8.6);
        var reason = VerdictEngine.Explain(edge, Verdict.Bet, isLive: true, KellyStaking.NoStake("no-positive-edge"));

        Assert.Contains(PoissonGoalModel.PaceCaveat, reason);
    }

    [Fact]
    public void Pre_match_explanations_do_not_claim_a_pace_assumption_that_was_never_made()
    {
        var reason = VerdictEngine.Explain(Edge(8.6), Verdict.Bet, isLive: false, KellyStaking.NoStake("x"));
        Assert.DoesNotContain(PoissonGoalModel.PaceCaveat, reason);
    }

    [Fact]
    public void A_positive_edge_with_negative_EV_is_explained_rather_than_left_looking_like_a_bug()
    {
        // This is the confusing case: the estimate beats the de-vigged fair line but not the price on
        // the screen. The verdict still says BET per the rules, and the stake is still zero, so the
        // reason has to reconcile the two.
        var edge = new EdgeResult(0.5139, 0.60, 8.6, -0.058);
        var reason = VerdictEngine.Explain(edge, Verdict.Bet, isLive: true, KellyStaking.NoStake("no-positive-edge"));

        Assert.Contains("8.6pp", reason);
        Assert.Contains("-5.8%", reason);
        Assert.Contains("No stake", reason);
    }

    [Fact]
    public void A_negative_edge_says_the_market_disagrees_rather_than_reporting_a_negative_edge_size()
    {
        var edge = new EdgeResult(0.60, 0.52, -8.0, -0.15);
        var reason = VerdictEngine.Explain(edge, Verdict.Skip, isLive: true, KellyStaking.NoStake("skip-below-threshold"));

        Assert.Contains("BELOW", reason);
        Assert.Contains("8.0pp", reason);
    }

    [Fact]
    public void A_recommended_stake_reports_which_rule_sized_it()
    {
        var stake = KellyStaking.RecommendStake(0.58, 2.00, 1000.0);
        var reason = VerdictEngine.Explain(Edge(8.0, ev: 0.16), Verdict.Bet, isLive: false, stake);

        Assert.Contains("quarter-kelly", reason);
        Assert.Contains("4.0%", reason);
    }
}
