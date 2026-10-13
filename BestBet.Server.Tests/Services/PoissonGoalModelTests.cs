using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class PoissonGoalModelTests
{
    private static LiveMatchState Live(
        int minute,
        double xgHome,
        double xgAway,
        int goalsHome = 0,
        int goalsAway = 0)
        => new(minute, xgHome, xgAway, goalsHome, goalsAway, IsPreMatch: false);

    [Theory]
    [InlineData(0, 1.0, 1.0)]       // e^-1
    [InlineData(1, 1.0, 1.0)]
    [InlineData(2, 1.0, 0.5)]
    [InlineData(3, 1.0, 1.0 / 6.0)]
    public void PoissonPmf_matches_the_closed_form(int k, double lambda, double factor)
        => Assert.Equal(Math.Exp(-lambda) * factor, PoissonGoalModel.PoissonPmf(k, lambda), 12);

    [Fact]
    public void PoissonPmf_of_a_zero_rate_puts_all_its_mass_on_zero_goals()
    {
        Assert.Equal(1.0, PoissonGoalModel.PoissonPmf(0, 0.0), 12);
        Assert.Equal(0.0, PoissonGoalModel.PoissonPmf(1, 0.0), 12);
    }

    [Fact]
    public void PoissonPmf_stays_finite_where_a_factorial_would_overflow()
    {
        // k = 35 with lambda = 3 is a vanishing probability, but the term-by-term build has to reach
        // it without 35! blowing up on the way.
        var p = PoissonGoalModel.PoissonPmf(35, 3.0);
        Assert.True(p is > 0 and < 1e-20, $"expected a tiny positive probability, got {p}");
    }

    [Theory]
    [InlineData(45, 45)]
    [InlineData(67, 23)]
    [InlineData(85, 5)]
    public void RemainingMinutes_is_the_rest_of_regulation_before_the_stoppage_cutoff(int minute, int expected)
        => Assert.Equal(expected, PoissonGoalModel.RemainingMinutes(minute));

    [Theory]
    [InlineData(86, 9)]     // 4 left in regulation, plus the stoppage allowance
    [InlineData(90, 5)]
    [InlineData(93, 5)]
    public void RemainingMinutes_adds_stoppage_time_once_past_minute_85(int minute, int expected)
    {
        // Without this, a match showing "90+2" would have zero minutes left and the model would say
        // no further goal is possible, which is plainly false.
        Assert.Equal(expected, PoissonGoalModel.RemainingMinutes(minute));
    }

    [Fact]
    public void ExtrapolateRemainingGoals_projects_each_side_at_its_own_observed_rate()
    {
        // 0.79 xG in 67 minutes is 0.0118 per minute; over the 23 that remain that is 0.271 goals.
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(Live(67, 0.79, 0.01, 1, 0));

        Assert.Equal(23, distribution.RemainingMinutes);
        Assert.Equal(0.2712, distribution.ExpectedRemainingHome, 4);
        Assert.Equal(0.0034, distribution.ExpectedRemainingAway, 4);
    }

    [Fact]
    public void ExtrapolateRemainingGoals_at_halftime_projects_the_whole_first_half_rate_again()
    {
        // Minute 45 is the clean case: 45 played, 45 left, so the remaining lambda equals the xG
        // accumulated so far.
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(Live(45, 0.13, 0.86));

        Assert.Equal(0.13, distribution.ExpectedRemainingHome, 10);
        Assert.Equal(0.86, distribution.ExpectedRemainingAway, 10);
    }

    [Fact]
    public void Each_sides_pmf_carries_essentially_all_of_its_probability_mass()
    {
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(Live(20, 1.4, 1.1));

        Assert.Equal(1.0, distribution.RemainingHomePmf.Sum(), 10);
        Assert.Equal(1.0, distribution.RemainingAwayPmf.Sum(), 10);
    }

    [Fact]
    public void The_convolved_total_is_itself_Poisson_with_the_two_rates_added()
    {
        // Independent Poisson processes add, so the convolution must agree with a single process at
        // the combined rate. This is the check that the convolution loop is right.
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(Live(30, 0.9, 0.6));
        var combined = distribution.ExpectedRemainingHome + distribution.ExpectedRemainingAway;
        var total = distribution.RemainingTotalPmf();

        for (var k = 0; k <= 8; k++)
        {
            Assert.Equal(PoissonGoalModel.PoissonPmf(k, combined), total[k], 10);
        }
    }

    [Fact]
    public void FinalTotalPmf_shifts_the_remaining_goals_up_by_the_current_score()
    {
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(Live(45, 0.5, 0.5, goalsHome: 2, goalsAway: 1));
        var final = distribution.FinalTotalPmf();

        // Three goals are already on the board, so fewer than three is now impossible.
        Assert.Equal(0.0, final[0], 12);
        Assert.Equal(0.0, final[1], 12);
        Assert.Equal(0.0, final[2], 12);
        Assert.Equal(distribution.RemainingTotalPmf()[0], final[3], 12);
        Assert.Equal(1.0, final.Sum(), 10);
    }

    [Fact]
    public void A_scoreless_rate_still_produces_a_valid_distribution()
    {
        // 0.00 xG means the model assigns exactly zero chance of scoring. That is wrong in reality,
        // and it is documented as a known limitation — but it must not produce NaN.
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(Live(10, 0.0, 0.0));

        Assert.Equal(1.0, distribution.RemainingHomePmf[0], 12);
        Assert.Equal(1.0, distribution.RemainingTotalPmf()[0], 12);
    }

    [Fact]
    public void Extrapolating_before_kickoff_is_refused_rather_than_faked()
    {
        var preMatch = new LiveMatchState(0, 0, 0, 0, 0, IsPreMatch: true);
        Assert.Throws<InvalidOperationException>(() => PoissonGoalModel.ExtrapolateRemainingGoals(preMatch));
    }

    [Fact]
    public void Extrapolating_from_zero_elapsed_minutes_is_refused_rather_than_dividing_by_zero()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PoissonGoalModel.ExtrapolateRemainingGoals(Live(0, 0.2, 0.1)));

    [Fact]
    public void Negative_xg_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PoissonGoalModel.ExtrapolateRemainingGoals(Live(30, -0.1, 0.2)));

    [Fact]
    public void The_pace_caveat_says_out_loud_which_way_the_model_is_wrong()
    {
        // This string is surfaced in the UI on every live result, so it is part of the contract.
        Assert.Contains("floor", PoissonGoalModel.PaceCaveat);
        Assert.Contains("pace", PoissonGoalModel.PaceCaveat);
    }
}
