using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class MarketProbabilityResolverTests
{
    private static GoalDistribution Distribution(
        int minute,
        double xgHome,
        double xgAway,
        int goalsHome = 0,
        int goalsAway = 0)
        => PoissonGoalModel.ExtrapolateRemainingGoals(
            new LiveMatchState(minute, xgHome, xgAway, goalsHome, goalsAway, IsPreMatch: false));

    private static double Resolve(MarketKind kind, MarketSelection side, GoalDistribution d, double? line = null)
        => MarketProbabilityResolver.Resolve(kind, new MarketSelectionSpec(side, line), d).Probability;

    // --- Next goal ----------------------------------------------------------

    [Fact]
    public void Next_goal_splits_the_remaining_time_between_two_competing_rates()
    {
        // Sunderland - Arsenal at half time: 0.13 home xG against 0.86 away. With 45 minutes left the
        // remaining rates are those same numbers, so P(no goal) = e^-0.99 and the rest splits 13:86.
        var d = Distribution(45, 0.13, 0.86);

        Assert.Equal(0.3716, Resolve(MarketKind.NextGoal, MarketSelection.NextGoalNone, d), 4);
        Assert.Equal(0.0825, Resolve(MarketKind.NextGoal, MarketSelection.NextGoalHome, d), 4);
        Assert.Equal(0.5459, Resolve(MarketKind.NextGoal, MarketSelection.NextGoalAway, d), 4);
    }

    [Fact]
    public void The_three_next_goal_outcomes_are_exhaustive()
    {
        var d = Distribution(60, 1.2, 0.4, 1, 1);

        var total = Resolve(MarketKind.NextGoal, MarketSelection.NextGoalHome, d)
                    + Resolve(MarketKind.NextGoal, MarketSelection.NextGoalAway, d)
                    + Resolve(MarketKind.NextGoal, MarketSelection.NextGoalNone, d);

        Assert.Equal(1.0, total, 10);
    }

    [Fact]
    public void The_team_creating_more_is_favoured_to_score_next()
    {
        var d = Distribution(45, 0.13, 0.86);

        Assert.True(Resolve(MarketKind.NextGoal, MarketSelection.NextGoalAway, d)
                    > Resolve(MarketKind.NextGoal, MarketSelection.NextGoalHome, d));
    }

    [Fact]
    public void With_no_time_and_no_xg_left_no_further_goal_is_a_certainty()
    {
        var d = Distribution(70, 0.0, 0.0);
        Assert.Equal(1.0, Resolve(MarketKind.NextGoal, MarketSelection.NextGoalNone, d), 10);
    }

    // --- Total goals --------------------------------------------------------

    [Fact]
    public void A_half_line_total_cannot_push_and_the_two_sides_sum_to_one()
    {
        var d = Distribution(67, 0.79, 0.01, 1, 0);

        var over = MarketProbabilityResolver.Resolve(
            MarketKind.TotalGoals, new MarketSelectionSpec(MarketSelection.Over, 2.5), d);
        var under = MarketProbabilityResolver.Resolve(
            MarketKind.TotalGoals, new MarketSelectionSpec(MarketSelection.Under, 2.5), d);

        Assert.Equal(0.0, over.PushProbability, 12);
        Assert.Equal(1.0, over.Probability + under.Probability, 10);
    }

    [Fact]
    public void A_whole_line_total_reports_its_push_and_prices_conditional_on_no_push()
    {
        // 1-0 after 67 minutes with 0.27 expected goals still to come: hitting exactly 4 is a 0.26%
        // push, and "Sub 4" is a near-certainty either way.
        var d = Distribution(67, 0.79, 0.01, 1, 0);
        var under = MarketProbabilityResolver.Resolve(
            MarketKind.TotalGoals, new MarketSelectionSpec(MarketSelection.Under, 4), d);

        Assert.Equal(0.0026, under.PushProbability, 4);
        Assert.Equal(0.9972, under.RawWinProbability, 4);
        Assert.Equal(0.9998, under.Probability, 4);
        Assert.Contains("push", under.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Goals_already_scored_count_towards_the_total()
    {
        // Three already on the board makes "over 2.5" settled, whatever happens next.
        var d = Distribution(50, 0.6, 0.5, goalsHome: 2, goalsAway: 1);
        Assert.Equal(1.0, Resolve(MarketKind.TotalGoals, MarketSelection.Over, d, 2.5), 10);
    }

    [Fact]
    public void A_higher_line_is_always_harder_to_go_over()
    {
        var d = Distribution(30, 0.8, 0.7);
        var lines = new[] { 0.5, 1.5, 2.5, 3.5, 4.5 };
        var probabilities = lines.Select(l => Resolve(MarketKind.TotalGoals, MarketSelection.Over, d, l)).ToArray();

        for (var i = 1; i < probabilities.Length; i++)
        {
            Assert.True(probabilities[i] < probabilities[i - 1]);
        }
    }

    [Fact]
    public void A_total_goals_market_without_a_line_is_refused()
    {
        var d = Distribution(30, 0.8, 0.7);
        Assert.Throws<ArgumentException>(() =>
            MarketProbabilityResolver.Resolve(
                MarketKind.TotalGoals, new MarketSelectionSpec(MarketSelection.Over, null), d));
    }

    // --- Both teams to score ------------------------------------------------

    [Fact]
    public void Both_teams_to_score_multiplies_the_two_independent_chances()
    {
        var d = Distribution(45, 0.9, 0.6);
        var expected = (1 - Math.Exp(-0.9)) * (1 - Math.Exp(-0.6));

        Assert.Equal(expected, Resolve(MarketKind.BothTeamsToScore, MarketSelection.Yes, d), 10);
        Assert.Equal(1.0 - expected, Resolve(MarketKind.BothTeamsToScore, MarketSelection.No, d), 10);
    }

    [Fact]
    public void A_team_that_has_already_scored_is_certain_to_have_scored()
    {
        // No model is needed for a goal that is already on the board, and treating it probabilistically
        // would quietly understate BTTS for the rest of the match.
        var d = Distribution(45, 0.9, 0.6, goalsHome: 1);
        var expected = 1 - Math.Exp(-0.6);

        Assert.Equal(expected, Resolve(MarketKind.BothTeamsToScore, MarketSelection.Yes, d), 10);
    }

    // --- 1X2 ----------------------------------------------------------------

    [Fact]
    public void The_three_result_outcomes_are_exhaustive()
    {
        var d = Distribution(60, 1.1, 0.7, 1, 1);

        var total = Resolve(MarketKind.Moneyline1X2, MarketSelection.Home, d)
                    + Resolve(MarketKind.Moneyline1X2, MarketSelection.Draw, d)
                    + Resolve(MarketKind.Moneyline1X2, MarketSelection.Away, d);

        Assert.Equal(1.0, total, 8);
    }

    [Fact]
    public void A_lead_late_on_dominates_the_result_probabilities()
    {
        // 2-0 up with ten minutes left: the home win should be overwhelming.
        var d = Distribution(80, 1.4, 0.3, goalsHome: 2, goalsAway: 0);
        Assert.True(Resolve(MarketKind.Moneyline1X2, MarketSelection.Home, d) > 0.95);
    }

    [Fact]
    public void A_level_game_with_identical_rates_is_symmetric()
    {
        var d = Distribution(45, 0.7, 0.7, 1, 1);

        Assert.Equal(
            Resolve(MarketKind.Moneyline1X2, MarketSelection.Home, d),
            Resolve(MarketKind.Moneyline1X2, MarketSelection.Away, d),
            10);
    }

    // --- Handicap -----------------------------------------------------------

    [Fact]
    public void A_zero_handicap_is_just_the_1X2_split()
    {
        var d = Distribution(60, 1.1, 0.7, 1, 0);

        Assert.Equal(
            Resolve(MarketKind.Moneyline1X2, MarketSelection.Home, d),
            MarketProbabilityResolver.Resolve(
                MarketKind.Handicap, new MarketSelectionSpec(MarketSelection.HandicapHome, 0), d).RawWinProbability,
            10);
    }

    [Fact]
    public void A_handicap_line_is_read_from_the_selected_teams_point_of_view()
    {
        // Home -1.5 and Away +1.5 are the two sides of the same bet, so they must add to one.
        var d = Distribution(55, 1.2, 0.5, 1, 0);

        var home = Resolve(MarketKind.Handicap, MarketSelection.HandicapHome, d, -1.5);
        var away = Resolve(MarketKind.Handicap, MarketSelection.HandicapAway, d, 1.5);

        Assert.Equal(1.0, home + away, 8);
    }

    [Fact]
    public void Giving_a_team_a_head_start_can_only_help_it()
    {
        var d = Distribution(55, 1.2, 0.5, 1, 0);

        Assert.True(Resolve(MarketKind.Handicap, MarketSelection.HandicapHome, d, 1.5)
                    > Resolve(MarketKind.Handicap, MarketSelection.HandicapHome, d, -1.5));
    }

    [Fact]
    public void A_whole_number_handicap_reports_the_push_it_can_produce()
    {
        var d = Distribution(55, 1.2, 0.5, 1, 0);
        var result = MarketProbabilityResolver.Resolve(
            MarketKind.Handicap, new MarketSelectionSpec(MarketSelection.HandicapHome, -1), d);

        Assert.True(result.PushProbability > 0);
        Assert.True(result.Probability > result.RawWinProbability);
    }

    // --- Cross-cutting ------------------------------------------------------

    [Fact]
    public void A_selection_from_the_wrong_market_is_refused_rather_than_silently_mispriced()
    {
        var d = Distribution(45, 0.5, 0.5);

        Assert.Throws<ArgumentException>(() =>
            MarketProbabilityResolver.Resolve(
                MarketKind.BothTeamsToScore, new MarketSelectionSpec(MarketSelection.Over, 2.5), d));
        Assert.Throws<ArgumentException>(() =>
            MarketProbabilityResolver.Resolve(
                MarketKind.NextGoal, new MarketSelectionSpec(MarketSelection.Home, null), d));
    }

    [Fact]
    public void Every_market_explains_where_its_number_came_from()
    {
        var d = Distribution(45, 0.5, 0.4);

        var descriptions = new[]
        {
            MarketProbabilityResolver.Resolve(MarketKind.NextGoal, new(MarketSelection.NextGoalHome, null), d).Description,
            MarketProbabilityResolver.Resolve(MarketKind.TotalGoals, new(MarketSelection.Over, 2.5), d).Description,
            MarketProbabilityResolver.Resolve(MarketKind.BothTeamsToScore, new(MarketSelection.Yes, null), d).Description,
            MarketProbabilityResolver.Resolve(MarketKind.Moneyline1X2, new(MarketSelection.Home, null), d).Description,
            MarketProbabilityResolver.Resolve(MarketKind.Handicap, new(MarketSelection.HandicapHome, -1.5), d).Description,
        };

        Assert.All(descriptions, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }
}
