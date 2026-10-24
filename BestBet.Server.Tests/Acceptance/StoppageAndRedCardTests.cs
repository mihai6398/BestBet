using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Acceptance;

/// <summary>
/// The two things a live price has to know that the xG on the clock does not tell it: how long the
/// referee will let the match run, and whether both sides still have eleven men.
/// </summary>
public class StoppageAndRedCardTests
{
    private readonly AnalysisService _service = new();

    private static PaceSettings StoppageOnly() => new(Shrink: false, ScoreState: false, Stoppage: true, RedCards: false);

    private static PaceSettings RedCardsOnly() => new(Shrink: false, ScoreState: false, Stoppage: false, RedCards: true);

    private static AnalyzeRequest Over(double line, LiveMatchState state, PaceSettings? pace = null) => new(
        new MarketOdds(MarketKind.TotalGoals, $"Peste {line}", 2.50, [1.50]),
        state,
        Bankroll: 100.0,
        Selection: MarketSelection.Over,
        Line: line,
        Pace: pace);

    // --- Stoppage -------------------------------------------------------------

    [Fact]
    public void A_match_running_at_the_league_average_still_expects_the_league_average_overall()
    {
        // The consistency check. A per-match prior spread over 90 minutes and then run over 97.5
        // would quietly add stoppage on top of numbers that already contain it.
        var perMinute = PaceModel.LeagueAverageGoalsPerTeam / MatchClock.TypicalMatchMinutes;
        var state = new LiveMatchState(20, perMinute * 20, perMinute * 20, 0, 0, false);
        var settings = new PaceSettings(Shrink: true, ScoreState: false, Stoppage: true, RedCards: false);

        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(state, settings);

        Assert.Equal(
            PaceModel.LeagueAverageGoalsPerTeam,
            state.CumulativeXgHome + distribution.ExpectedRemainingHome,
            10);
    }

    [Fact]
    public void The_board_at_90_moves_a_late_over()
    {
        // 1-1 at minute 88, needing one more for Over 2.5. Two minutes of regulation left, and the
        // board is what decides how much more there is.
        var quiet = _service.Analyze(Over(2.5, new LiveMatchState(88, 1.4, 1.2, 1, 1, false, MatchPeriod.SecondHalf, 2, 2)));
        var long_ = _service.Analyze(Over(2.5, new LiveMatchState(88, 1.4, 1.2, 1, 1, false, MatchPeriod.SecondHalf, 2, 8)));

        Assert.Equal(4.5, quiet.Model!.RemainingMinutes, 10);
        Assert.Equal(10.5, long_.Model!.RemainingMinutes, 10);
        Assert.True(long_.Edge.OwnProbability > quiet.Edge.OwnProbability * 1.8);
    }

    [Fact]
    public void Counting_stoppage_lifts_a_late_over_the_specs_clock_prices_too_low()
    {
        // Minute 80: ten minutes on the spec's clock, fourteen and a half in an average match.
        var state = new LiveMatchState(80, 1.1, 0.9, 1, 1, false);
        var spec = _service.Analyze(Over(2.5, state, PaceSettings.Flat));
        var counted = _service.Analyze(Over(2.5, state, StoppageOnly()));

        Assert.Equal(10, spec.Model!.RemainingMinutes);
        Assert.Equal(14.5, counted.Model!.RemainingMinutes, 10);
        Assert.True(counted.Edge.OwnProbability > spec.Edge.OwnProbability);
    }

    [Fact]
    public void Flat_settings_ignore_the_board_and_keep_the_specs_clock()
    {
        var state = new LiveMatchState(88, 1.4, 1.2, 1, 1, false, MatchPeriod.SecondHalf, 2, 8);
        var pace = PaceModel.Adjust(state, PaceSettings.Flat);

        Assert.Equal(PoissonGoalModel.RemainingMinutes(88), pace.RemainingMinutes);
        Assert.Equal(88, pace.ElapsedMinutes);
    }

    [Fact]
    public void An_impossible_board_is_refused()
    {
        var state = new LiveMatchState(88, 1.4, 1.2, 1, 1, false, MatchPeriod.SecondHalf, 2, 45);

        var ex = Assert.Throws<AnalysisInputException>(() => _service.Analyze(Over(2.5, state)));
        Assert.Contains("fourth official", ex.Message);
    }

    // --- Red cards ------------------------------------------------------------

    [Fact]
    public void A_red_card_just_shown_cuts_one_side_and_lifts_the_other_in_full()
    {
        var state = new LiveMatchState(60, 1.2, 0.6, 0, 0, false, RedCardsHome: 1, LastRedCardMinute: 60);
        var pace = PaceModel.Adjust(state, RedCardsOnly());

        Assert.Equal(pace.RawRateHome * PaceModel.ShortHandedRateFactor, pace.AdjustedRateHome, 12);
        Assert.Equal(pace.RawRateAway * PaceModel.AgainstShortHandedRateFactor, pace.AdjustedRateAway, 12);
        Assert.Contains(pace.Steps, s => s.Contains("Home side down to 10 men since minute 60"));
    }

    [Fact]
    public void A_card_with_no_minute_is_treated_as_just_shown()
    {
        var known = PaceModel.Adjust(
            new LiveMatchState(60, 1.2, 0.6, 0, 0, false, RedCardsAway: 1, LastRedCardMinute: 60), RedCardsOnly());
        var unknown = PaceModel.Adjust(
            new LiveMatchState(60, 1.2, 0.6, 0, 0, false, RedCardsAway: 1), RedCardsOnly());

        Assert.Equal(known.AdjustedRateAway, unknown.AdjustedRateAway, 12);
        Assert.Equal(known.AdjustedRateHome, unknown.AdjustedRateHome, 12);
    }

    [Fact]
    public void A_card_from_kick_off_is_already_in_every_minute_of_the_xG()
    {
        // Every minute of evidence was played ten against eleven, so the observed rate already is the
        // rate with a man down. Correcting it again would count the card twice.
        var pace = PaceModel.Adjust(
            new LiveMatchState(60, 1.2, 0.6, 0, 0, false, RedCardsHome: 1, LastRedCardMinute: 0), RedCardsOnly());

        Assert.Equal(pace.RawRateHome, pace.AdjustedRateHome, 12);
        Assert.Equal(pace.RawRateAway, pace.AdjustedRateAway, 12);
    }

    [Fact]
    public void An_older_card_moves_the_rate_less_than_a_new_one()
    {
        var early = PaceModel.Adjust(
            new LiveMatchState(80, 1.2, 0.6, 0, 0, false, RedCardsHome: 1, LastRedCardMinute: 20), PaceSettings.Default);
        var late = PaceModel.Adjust(
            new LiveMatchState(80, 1.2, 0.6, 0, 0, false, RedCardsHome: 1, LastRedCardMinute: 78), PaceSettings.Default);

        Assert.True(early.AdjustedRateHome > late.AdjustedRateHome);
        Assert.True(early.AdjustedRateAway < late.AdjustedRateAway);
    }

    [Fact]
    public void A_red_card_each_cancels_out()
    {
        var pace = PaceModel.Adjust(
            new LiveMatchState(60, 1.2, 0.6, 0, 0, false, RedCardsHome: 1, RedCardsAway: 1), RedCardsOnly());

        Assert.Equal(pace.RawRateHome, pace.AdjustedRateHome, 12);
        Assert.Empty(pace.Steps);
    }

    [Fact]
    public void Two_men_down_compounds()
    {
        var pace = PaceModel.Adjust(
            new LiveMatchState(70, 1.2, 0.6, 0, 0, false, RedCardsAway: 2, LastRedCardMinute: 70), RedCardsOnly());

        Assert.Equal(pace.RawRateAway * 0.49, pace.AdjustedRateAway, 12);
        Assert.Contains(pace.Steps, s => s.Contains("down to 9 men"));
    }

    [Fact]
    public void A_red_card_makes_the_side_with_eleven_more_likely_to_score_next()
    {
        var level = _service.Analyze(NextGoalHome(new LiveMatchState(55, 0.8, 0.8, 0, 0, false)));
        var tenMen = _service.Analyze(NextGoalHome(new LiveMatchState(55, 0.8, 0.8, 0, 0, false, RedCardsAway: 1)));

        Assert.True(tenMen.Edge.OwnProbability > level.Edge.OwnProbability);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 5)]
    public void An_impossible_number_of_red_cards_is_refused(int home, int away)
    {
        var state = new LiveMatchState(60, 1.2, 0.6, 0, 0, false, RedCardsHome: home, RedCardsAway: away);

        var ex = Assert.Throws<AnalysisInputException>(() => _service.Analyze(Over(2.5, state)));
        Assert.Contains("Red cards", ex.Message);
    }

    private static AnalyzeRequest NextGoalHome(LiveMatchState state) => new(
        new MarketOdds(MarketKind.NextGoal, "Gol următor: 1", 2.20, [3.40, 4.50]),
        state,
        Bankroll: 100.0,
        Selection: MarketSelection.NextGoalHome);
}
