using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Acceptance;

/// <summary>
/// Shrinking the live pace towards what these two teams usually produce, rather than towards a
/// league-average match.
/// </summary>
/// <remarks>
/// With one league-wide prior every side is treated as mid-table, so a top attack on a genuine
/// 2.6-goal pace gets pulled down towards 1.35 — a lean towards the Under that is strongest on exactly
/// the matches where the Over is most often right. The pre-match model already knows who is playing.
/// These tests pin both directions: a strong attack is no longer dragged down, and two defensive
/// sides are pulled down further than before. The fix is "believe the teams", not "prefer the Over".
/// </remarks>
public class TeamPriorTests
{
    private readonly AnalysisService _service = new();

    /// <summary>A top home attack against a weak one: 1-0 at minute 30, xG 0.85 against 0.20.</summary>
    private static LiveMatchState TopSideCruising() => new(30, 0.85, 0.20, 1, 0, IsPreMatch: false);

    /// <summary>What the pre-match model would give a side like that, at home to a side like that.</summary>
    private static readonly PreMatchRates TopSideAtHome = new(2.60, 0.90);

    /// <summary>Two sides that rarely score or concede, on a livelier start than usual: 0-0, xG 0.60 / 0.50.</summary>
    private static LiveMatchState TwoDefensiveSides() => new(30, 0.60, 0.50, 0, 0, IsPreMatch: false);

    private static readonly PreMatchRates DefensiveFixture = new(0.90, 0.80);

    private static PaceSettings Shrinkage() => new(Shrink: true, ScoreState: false);

    private static AnalyzeRequest Over35(LiveMatchState state, PreMatchRates? preMatch) => new(
        new MarketOdds(MarketKind.TotalGoals, "Peste 3.5", 2.10, [1.75]),
        state,
        Bankroll: 100.0,
        Selection: MarketSelection.Over,
        Line: 3.5,
        PreMatch: preMatch);

    // --- The case that motivated it -----------------------------------------

    [Fact]
    public void A_top_attack_is_pulled_towards_its_own_numbers_not_towards_mid_table()
    {
        var league = PaceModel.Adjust(TopSideCruising(), Shrinkage());
        var team = PaceModel.Adjust(TopSideCruising(), Shrinkage(), TopSideAtHome);

        // Against the league average the home side's 2.55-per-90 pace is dragged down to about 1.83;
        // against its own 2.60 it is left at about 2.58, which is what it was actually doing.
        Assert.Equal(1.83, league.AdjustedRateHome * PoissonGoalModel.RegulationMinutes, 2);
        Assert.Equal(2.58, team.AdjustedRateHome * PoissonGoalModel.RegulationMinutes, 2);
        Assert.True(team.AdjustedRateTotal > league.AdjustedRateTotal);
    }

    [Fact]
    public void The_over_is_priced_higher_when_the_teams_are_expected_to_score()
    {
        var withoutPrior = _service.Analyze(Over35(TopSideCruising(), preMatch: null));
        var withPrior = _service.Analyze(Over35(TopSideCruising(), TopSideAtHome));

        Assert.True(withPrior.Edge.OwnProbability > withoutPrior.Edge.OwnProbability);
    }

    [Fact]
    public void It_cuts_both_ways_two_defensive_sides_make_the_over_less_likely()
    {
        // The honest half of the fix. A lively start between two sides that rarely score is pulled
        // down harder than a league-average prior would pull it, so this does not just lean Over.
        var withoutPrior = _service.Analyze(Over35(TwoDefensiveSides(), preMatch: null));
        var withPrior = _service.Analyze(Over35(TwoDefensiveSides(), DefensiveFixture));

        Assert.True(withPrior.Edge.OwnProbability < withoutPrior.Edge.OwnProbability);
    }

    [Fact]
    public void A_prior_equal_to_the_league_average_changes_nothing()
    {
        // The team prior generalises the old behaviour rather than replacing it: hand it the league
        // average and the numbers have to come out identical.
        var league = PaceModel.Adjust(TopSideCruising(), Shrinkage());
        var same = PaceModel.Adjust(
            TopSideCruising(),
            Shrinkage(),
            new PreMatchRates(PaceModel.LeagueAverageGoalsPerTeam, PaceModel.LeagueAverageGoalsPerTeam));

        Assert.Equal(league.AdjustedRateHome, same.AdjustedRateHome, 12);
        Assert.Equal(league.AdjustedRateAway, same.AdjustedRateAway, 12);
    }

    [Fact]
    public void The_evidence_still_wins_as_the_match_goes_on()
    {
        // The prior is worth 45 minutes of play whichever prior it is. By minute 80 a side running
        // far below its pre-match number is mostly judged on what it has actually done.
        var quietTopSide = new LiveMatchState(80, 0.40, 0.30, 0, 0, IsPreMatch: false);

        var pace = PaceModel.Adjust(quietTopSide, Shrinkage(), TopSideAtHome);

        var adjustedPer90 = pace.AdjustedRateHome * PoissonGoalModel.RegulationMinutes;
        Assert.True(adjustedPer90 < 1.30, $"expected the quiet 80 minutes to dominate, got {adjustedPer90:0.00}/90");
    }

    // --- Saying what happened ----------------------------------------------

    [Fact]
    public void The_explanation_says_the_pace_was_pulled_towards_the_pre_match_numbers()
    {
        var pace = PaceModel.Adjust(TopSideCruising(), Shrinkage(), TopSideAtHome);

        Assert.True(pace.PriorFromPreMatch);
        Assert.Equal(2.60, pace.PriorGoalsHome);
        Assert.Equal(0.90, pace.PriorGoalsAway);
        Assert.Contains(pace.Steps, step => step.Contains("pre-match expectation of 2.6 xG home and 0.9 away"));
    }

    [Fact]
    public void Without_a_prior_the_explanation_still_names_the_league_average()
    {
        var pace = PaceModel.Adjust(TopSideCruising(), Shrinkage());

        Assert.False(pace.PriorFromPreMatch);
        Assert.Equal(PaceModel.LeagueAverageGoalsPerTeam, pace.PriorGoalsHome);
        Assert.Contains(pace.Steps, step => step.Contains("league-average 1.35 xG per team"));
    }

    [Fact]
    public void The_estimate_is_tagged_so_the_log_can_tell_the_two_models_apart()
    {
        var withPrior = _service.Analyze(Over35(TopSideCruising(), TopSideAtHome));
        var withoutPrior = _service.Analyze(Over35(TopSideCruising(), preMatch: null));

        Assert.Equal(AnalysisService.LivePreMatchPriorMethod, withPrior.Estimate.Method);
        Assert.Equal("poisson-live-xg-paced", withoutPrior.Estimate.Method);
    }

    [Fact]
    public void With_shrinkage_off_the_prior_has_nothing_to_act_on()
    {
        var pace = PaceModel.Adjust(TopSideCruising(), PaceSettings.Flat, TopSideAtHome);

        Assert.False(pace.PriorFromPreMatch);
        Assert.Null(pace.PriorGoalsHome);
        Assert.Equal(pace.RawRateHome, pace.AdjustedRateHome, 12);
    }

    // --- Inputs -------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.2, -0.5)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NaN, 1.0)]
    public void A_prior_that_is_not_a_positive_number_is_refused(double home, double away)
    {
        var request = Over35(TopSideCruising(), new PreMatchRates(home, away));

        var refused = Assert.Throws<AnalysisInputException>(() => _service.Analyze(request));
        Assert.Contains("pre-match expected goals", refused.Message);
    }
}
