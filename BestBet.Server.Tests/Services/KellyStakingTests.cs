using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class KellyStakingTests
{
    private const double Bankroll = 1000.0;

    [Theory]
    [InlineData(0.60, 2.00, 0.20)]
    [InlineData(0.58, 2.00, 0.16)]
    [InlineData(0.50, 2.00, 0.00)]
    [InlineData(0.45, 2.00, -0.10)]
    public void FullKelly_is_the_textbook_formula(double p, double odds, double expected)
        => Assert.Equal(expected, KellyStaking.FullKelly(p, odds), 10);

    [Fact]
    public void A_stake_inside_the_band_is_left_at_quarter_Kelly()
    {
        // Full Kelly here is 16%; a quarter of that is 4%, which sits inside 3–5% untouched.
        var stake = KellyStaking.RecommendStake(0.58, 2.00, Bankroll);

        Assert.Equal("quarter-kelly", stake.CapReason);
        Assert.Equal(0.04, stake.SuggestedStakeFraction, 10);
        Assert.Equal(40.0, stake.SuggestedStakeAmount, 10);
    }

    [Fact]
    public void A_thin_edge_is_lifted_to_the_three_percent_floor()
    {
        // Quarter Kelly on a 10% full-Kelly edge is 2.5%, below the floor.
        var stake = KellyStaking.RecommendStake(0.55, 2.00, Bankroll);

        Assert.Equal("floor-3pct", stake.CapReason);
        Assert.Equal(0.03, stake.SuggestedStakeFraction, 10);
        Assert.Equal(30.0, stake.SuggestedStakeAmount, 10);
    }

    [Fact]
    public void A_fat_edge_is_cut_back_to_the_five_percent_cap()
    {
        // Quarter Kelly would want 7.5% of bankroll here. The cap exists because the probability
        // estimate is never as good as Kelly assumes it is.
        var stake = KellyStaking.RecommendStake(0.65, 2.00, Bankroll);

        Assert.Equal("cap-5pct", stake.CapReason);
        Assert.Equal(0.05, stake.SuggestedStakeFraction, 10);
        Assert.Equal(50.0, stake.SuggestedStakeAmount, 10);
    }

    [Fact]
    public void No_stake_is_ever_larger_than_the_cap_however_big_the_edge_looks()
    {
        foreach (var p in new[] { 0.70, 0.85, 0.95, 0.999 })
        {
            var stake = KellyStaking.RecommendStake(p, 2.00, Bankroll);
            Assert.True(stake.SuggestedStakeFraction <= KellyStaking.MaximumStakeFraction);
        }
    }

    [Theory]
    [InlineData(0.45, 2.00)]   // clearly negative
    [InlineData(0.50, 2.00)]   // exactly break-even
    [InlineData(0.60, 1.57)]   // beats the de-vigged line, does not beat the price on offer
    public void No_positive_edge_means_no_stake_at_all_rather_than_a_small_one(double p, double odds)
    {
        var stake = KellyStaking.RecommendStake(p, odds, Bankroll);

        Assert.Equal(0.0, stake.SuggestedStakeFraction);
        Assert.Equal(0.0, stake.SuggestedStakeAmount);
        Assert.Equal("no-positive-edge", stake.CapReason);
    }

    [Fact]
    public void The_floor_does_not_resurrect_a_bet_with_no_edge()
    {
        // The 3% floor applies to bets worth making, not to every bet. Clamping first and checking
        // the edge second would turn every losing proposition into a 3% position.
        var stake = KellyStaking.RecommendStake(0.40, 2.00, Bankroll);
        Assert.Equal(0.0, stake.SuggestedStakeAmount);
    }

    [Fact]
    public void A_zero_bankroll_yields_a_zero_amount_but_keeps_the_fraction()
    {
        var stake = KellyStaking.RecommendStake(0.58, 2.00, 0.0);

        Assert.Equal(0.04, stake.SuggestedStakeFraction, 10);
        Assert.Equal(0.0, stake.SuggestedStakeAmount, 10);
    }

    [Fact]
    public void NoStake_carries_the_reason_the_caller_rejected_the_bet()
    {
        var stake = KellyStaking.NoStake("skip-below-threshold");

        Assert.Equal(0.0, stake.SuggestedStakeAmount);
        Assert.Equal("skip-below-threshold", stake.CapReason);
    }

    [Fact]
    public void Prices_at_or_below_evens_return_are_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => KellyStaking.FullKelly(0.6, 1.0));
}
