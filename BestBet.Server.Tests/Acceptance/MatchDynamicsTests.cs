using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Acceptance;

/// <summary>
/// The pace corrections, and the real match that motivated them.
/// </summary>
/// <remarks>
/// Barcelona were 3-1 up on Racing Club at minute 36 with 2.38 xG against 0.30. The flat model in the
/// spec priced Over 6.5 at 76.5% against a market showing 1.91 / 1.91 — a fair 50% — and called it a
/// BET with a 26.5pp edge and a capped stake. It was wrong, and wrong in the most dangerous possible
/// direction: the bigger the model's error, the more attractive the bet looked.
/// </remarks>
public class MatchDynamicsTests
{
    private readonly AnalysisService _service = new();

    /// <summary>Barcelona 3-1 Racing Club, minute 36.</summary>
    private static LiveMatchState BarcelonaRacing() => new(36, 2.38, 0.30, 3, 1, IsPreMatch: false);

    private static AnalyzeRequest Over65(PaceSettings? pace) => new(
        new MarketOdds(MarketKind.TotalGoals, "Peste 6.5", 1.91, [1.91]),
        BarcelonaRacing(),
        Bankroll: 263.0,
        Selection: MarketSelection.Over,
        Line: 6.5,
        Pace: pace);

    // --- The case that started it -------------------------------------------

    [Fact]
    public void The_flat_model_prices_a_hot_start_as_a_large_edge()
    {
        // Kept as a test rather than deleted: this is the failure mode, and it has to stay visible.
        var response = _service.Analyze(Over65(PaceSettings.Flat));

        Assert.Equal(0.7648, response.Edge.OwnProbability, 4);
        Assert.Equal(0.50, response.Edge.FairMarketProbability, 4);
        Assert.Equal(26.5, response.Edge.EdgePercentagePoints, 1);
        Assert.Equal(Verdict.Bet, response.Verdict);
        Assert.Equal(13.15, response.Stake.SuggestedStakeAmount, 2);
    }

    [Fact]
    public void The_pace_corrections_turn_that_same_position_into_a_skip()
    {
        // Shrinkage and score state on the spec's clock, so this pins what those two do on their own.
        var response = _service.Analyze(Over65(Rates()));

        Assert.Equal(0.4755, response.Edge.OwnProbability, 4);
        Assert.Equal(-2.45, response.Edge.EdgePercentagePoints, 2);
        Assert.Equal(Verdict.Skip, response.Verdict);
        Assert.Equal(0.0, response.Stake.SuggestedStakeAmount);
    }

    [Fact]
    public void Counting_stoppage_raises_it_again_but_not_to_a_bet()
    {
        // The spec's clock gives this match 54 minutes left at minute 36. About 61 will be played:
        // nine to half time, the three or so a first half runs over, forty-five, and four and a half
        // more after ninety. Seven and a half extra minutes for a side scoring at this rate is worth
        // nearly seven points on the Over — which is why the clock is worth getting right, and why
        // the old clock leant towards the Under on every match it priced. Still under the 5pp a live
        // bet needs: the 26.5pp the flat model claimed is not coming back.
        var response = _service.Analyze(Over65(new PaceSettings(TimeProfile: false, RateUncertainty: false)));

        Assert.Equal(61.5, response.Model!.RemainingMinutes, 10);
        Assert.Equal(0.5435, response.Edge.OwnProbability, 4);
        Assert.Equal(4.35, response.Edge.EdgePercentagePoints, 2);
        Assert.Equal(Verdict.Marginal, response.Verdict);
    }

    [Fact]
    public void Counting_the_tempo_puts_more_of_the_over_back_and_the_timing_advice_holds_it()
    {
        // The minutes left here include a whole second half, the liveliest part of a match, and the
        // 36 played were quieter than an average 36 — so the tempo raises the Over again, past the
        // live threshold. That is the honest output of a correction that is right on average, and it
        // is exactly the position the timing advice exists for: a pace of 5.95 xG per ninety is the
        // thing the shrinkage guessed at, and a bet that needs it to hold is a bet on the guess.
        var response = _service.Analyze(Over65(pace: null));

        Assert.Equal(0.5871, response.Edge.OwnProbability, 4);
        Assert.Equal(Verdict.Bet, response.Verdict);
        Assert.Equal(TimingAction.Wait, response.Timing!.Action);
        Assert.Equal(TimingAdvisor.HotStart, Assert.Single(response.Timing.Reasons).Code);
    }

    [Fact]
    public void Correction_is_what_you_get_without_asking_for_it()
    {
        // The flat model is the one you have to opt into now, not the other way round. The cases it
        // is most wrong about are the ones that look most like a bet, so it cannot be the default.
        var response = _service.Analyze(Over65(pace: null));

        Assert.True(response.Model!.Pace.IsAdjusted);
        Assert.Equal("poisson-live-xg-paced", response.Estimate.Method);
    }

    [Fact]
    public void The_response_says_the_raw_projection_was_implausible()
    {
        var response = _service.Analyze(Over65(pace: null));
        var pace = response.Model!.Pace;

        // 2.38 xG in 36 minutes is 5.95 over ninety. Almost no match reaches four for one side.
        Assert.Equal(5.95, pace.ProjectedFullMatchXgHome, 2);
        Assert.NotNull(pace.Warning);
        Assert.Contains("5.95", pace.Warning);
    }

    [Fact]
    public void Every_correction_that_fired_is_reported_rather_than_applied_silently()
    {
        var pace = _service.Analyze(Over65(pace: null)).Model!.Pace;

        Assert.Equal(5, pace.Steps.Count);
        Assert.Contains(pace.Steps, s => s.Contains("league-average"));
        Assert.Contains(pace.Steps, s => s.Contains("leads by 2"));
        Assert.Contains(pace.Steps, s => s.Contains("Tempo counted"));
        Assert.Contains(pace.Steps, s => s.Contains("Stoppage counted: 61.5 minutes left rather than 54"));
        Assert.Contains(pace.Steps, s => s.Contains("an estimate, not a fact"));
    }

    // --- The caveat that used to read backwards -----------------------------

    [Fact]
    public void A_corrected_down_pace_says_so_instead_of_calling_itself_a_floor()
    {
        // The spec's caveat — "treat this as a floor, not a ceiling" — is actively misleading here.
        // The model was too HIGH, not too low.
        var response = _service.Analyze(Over65(pace: null));

        Assert.True(response.Model!.Pace.PaceMultiplier < 0.95);
        Assert.Contains("corrected DOWN", response.VerdictReason);
        Assert.DoesNotContain(PoissonGoalModel.PaceCaveat, response.VerdictReason);
    }

    [Fact]
    public void A_corrected_up_pace_still_says_it_is_a_floor()
    {
        // The quiet-match case the spec was written for: 0-0 at minute 76 on modest xG. Here the
        // correction raises the rate, and "a floor, not a ceiling" is the honest reading again.
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 1.5", 2.10, [1.75]),
            new LiveMatchState(76, 0.35, 0.20, 0, 0, IsPreMatch: false),
            Bankroll: 263.0,
            Selection: MarketSelection.Over,
            Line: 1.5));

        Assert.True(response.Model!.Pace.PaceMultiplier > 1.05);
        Assert.Contains("corrected UP", response.VerdictReason);
        Assert.Contains("floor rather than a ceiling", response.VerdictReason);
    }

    // --- Shrinkage ----------------------------------------------------------

    [Fact]
    public void Shrinkage_pulls_a_hot_rate_down_and_a_cold_one_up()
    {
        // The same correction, in both directions, in one match: Barcelona's 5.95-per-90 pace comes
        // down and Racing's 0.75 goes up. A quiet half hour is not proof of a quiet match either.
        var pace = PaceModel.Adjust(
            BarcelonaRacing(),
            new PaceSettings(Shrink: true, ScoreState: false));

        Assert.True(pace.AdjustedRateHome < pace.RawRateHome);
        Assert.True(pace.AdjustedRateAway > pace.RawRateAway);
    }

    [Fact]
    public void Shrinkage_matters_less_the_longer_the_match_has_run()
    {
        // Ten minutes of football tells you very little; eighty tells you a lot. The correction has
        // to fade accordingly, or it would still be arguing with the evidence at full time.
        var early = PaceModel.Adjust(new LiveMatchState(10, 0.80, 0.10, 1, 0, false), Shrinkage());
        var late = PaceModel.Adjust(new LiveMatchState(80, 6.40, 0.80, 1, 0, false), Shrinkage());

        // Both are running at the same rate per minute; only the sample size differs.
        Assert.Equal(early.RawRateHome, late.RawRateHome, 10);
        Assert.True(early.PaceMultiplier < late.PaceMultiplier);
    }

    [Fact]
    public void A_match_running_at_the_league_average_is_left_alone()
    {
        // 1.35 xG per team over ninety is exactly the prior, so there is nothing to correct towards.
        var pace = PaceModel.Adjust(new LiveMatchState(45, 0.675, 0.675, 0, 0, false), Shrinkage());

        Assert.Equal(pace.RawRateHome, pace.AdjustedRateHome, 10);
        Assert.Equal(1.0, pace.PaceMultiplier, 10);
    }

    // --- Score state --------------------------------------------------------

    [Fact]
    public void The_leader_attacks_less_and_the_chaser_attacks_more()
    {
        var pace = PaceModel.Adjust(new LiveMatchState(70, 1.0, 1.0, 2, 0, false), ScoreState());

        Assert.True(pace.AdjustedRateHome < pace.RawRateHome);
        Assert.True(pace.AdjustedRateAway > pace.RawRateAway);
    }

    [Fact]
    public void The_same_lead_matters_more_the_later_it_is()
    {
        var early = PaceModel.Adjust(new LiveMatchState(20, 0.5, 0.5, 1, 0, false), ScoreState());
        var late = PaceModel.Adjust(new LiveMatchState(85, 0.5, 0.5, 1, 0, false), ScoreState());

        var earlyCut = 1 - early.AdjustedRateHome / early.RawRateHome;
        var lateCut = 1 - late.AdjustedRateHome / late.RawRateHome;

        Assert.True(lateCut > earlyCut * 2);
    }

    [Fact]
    public void A_level_match_gets_no_score_state_correction()
    {
        var pace = PaceModel.Adjust(new LiveMatchState(70, 1.0, 0.8, 1, 1, false), ScoreState());

        Assert.False(pace.IsAdjusted);
        Assert.Empty(pace.Steps);
        Assert.Equal(pace.RawRateHome, pace.AdjustedRateHome, 12);
    }

    [Fact]
    public void A_rout_is_treated_no_differently_from_a_three_goal_lead()
    {
        // 6-0 and 3-0 are both just "over". Letting the effect grow without limit would eventually
        // drive the leader's rate negative.
        var three = PaceModel.Adjust(new LiveMatchState(70, 1.0, 1.0, 3, 0, false), ScoreState());
        var six = PaceModel.Adjust(new LiveMatchState(70, 1.0, 1.0, 6, 0, false), ScoreState());

        Assert.Equal(three.AdjustedRateHome, six.AdjustedRateHome, 10);
    }

    [Fact]
    public void The_correction_works_the_same_way_for_an_away_lead()
    {
        var pace = PaceModel.Adjust(new LiveMatchState(70, 1.0, 1.0, 0, 2, false), ScoreState());

        Assert.True(pace.AdjustedRateAway < pace.RawRateAway);
        Assert.True(pace.AdjustedRateHome > pace.RawRateHome);
        Assert.Contains(pace.Steps, s => s.Contains("Away leads by 2"));
    }

    // --- Recency ------------------------------------------------------------

    [Fact]
    public void A_recent_window_pulls_the_rate_towards_what_the_match_is_doing_now()
    {
        // 2.0 xG in the first half then 0.1 in fifteen quiet minutes: the flat average still says
        // 0.035/min, but the game has clearly died.
        var state = new LiveMatchState(60, 2.10, 0.50, 1, 0, false);
        var settings = new PaceSettings(
            Shrink: false,
            ScoreState: false,
            TimeProfile: false,
            RateUncertainty: false,
            RecentWindowMinutes: 15,
            RecentXgHome: 0.10,
            RecentXgAway: 0.05);

        var pace = PaceModel.Adjust(state, settings);

        Assert.True(pace.AdjustedRateHome < pace.RawRateHome);
        Assert.Contains(pace.Steps, s => s.Contains("Last 15 minutes"));
    }

    [Fact]
    public void A_recent_window_can_raise_the_rate_when_a_match_opens_up()
    {
        var state = new LiveMatchState(60, 0.60, 0.40, 0, 0, false);
        var settings = new PaceSettings(
            Shrink: false,
            ScoreState: false,
            TimeProfile: false,
            RateUncertainty: false,
            RecentWindowMinutes: 15,
            RecentXgHome: 0.45,
            RecentXgAway: 0.30);

        var pace = PaceModel.Adjust(state, settings);

        Assert.True(pace.AdjustedRateHome > pace.RawRateHome);
        Assert.True(pace.PaceMultiplier > 1.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void The_recency_weight_spans_from_ignoring_the_window_to_using_only_it(double weight)
    {
        var state = new LiveMatchState(60, 2.10, 0.50, 0, 0, false);
        var settings = new PaceSettings(
            Shrink: false,
            ScoreState: false,
            TimeProfile: false,
            RateUncertainty: false,
            RecentWindowMinutes: 15,
            RecentXgHome: 0.10,
            RecentXgAway: 0.05,
            RecencyWeight: weight);

        var pace = PaceModel.Adjust(state, settings);
        var expected = weight == 0.0 ? pace.RawRateHome : 0.10 / 15;

        Assert.Equal(expected, pace.AdjustedRateHome, 10);
    }

    [Fact]
    public void A_recent_window_longer_than_the_match_is_refused()
    {
        var settings = new PaceSettings(RecentWindowMinutes: 60, RecentXgHome: 0.1, RecentXgAway: 0.1);
        var ex = Assert.Throws<ArgumentException>(
            () => PaceModel.Adjust(new LiveMatchState(30, 0.5, 0.5, 0, 0, false), settings));

        Assert.Contains("cannot be longer than", ex.Message);
    }

    [Fact]
    public void A_recent_window_holding_more_xg_than_the_whole_match_is_refused()
    {
        // The usual cause is reading the FIRST-half column instead of subtracting it. The message
        // says so rather than leaving the reader to work it out.
        var settings = new PaceSettings(RecentWindowMinutes: 15, RecentXgHome: 3.0, RecentXgAway: 0.1);
        var ex = Assert.Throws<ArgumentException>(
            () => PaceModel.Adjust(new LiveMatchState(60, 2.0, 0.5, 0, 0, false), settings));

        Assert.Contains("total minus first half", ex.Message);
    }

    [Fact]
    public void An_incomplete_recent_window_is_ignored_rather_than_half_applied()
    {
        // Half a window is not data. Using it with a missing side defaulted to zero would invent a
        // collapse in scoring that nobody reported.
        var settings = new PaceSettings(
            Shrink: false, ScoreState: false, TimeProfile: false, RateUncertainty: false, RecentWindowMinutes: 15);
        var pace = PaceModel.Adjust(new LiveMatchState(60, 2.0, 0.5, 0, 0, false), settings);

        Assert.Equal(pace.RawRateHome, pace.AdjustedRateHome, 12);
        Assert.DoesNotContain(pace.Steps, s => s.Contains("Last 15 minutes"));
    }

    // --- Flat mode is still exactly the spec --------------------------------

    [Fact]
    public void Flat_settings_reproduce_the_specs_extrapolation_byte_for_byte()
    {
        var state = BarcelonaRacing();

        var viaPace = PoissonGoalModel.ExtrapolateRemainingGoals(state, PaceSettings.Flat);
        var viaSpec = PoissonGoalModel.ExtrapolateRemainingGoals(state);

        Assert.Equal(viaSpec.ExpectedRemainingHome, viaPace.ExpectedRemainingHome, 12);
        Assert.Equal(viaSpec.ExpectedRemainingAway, viaPace.ExpectedRemainingAway, 12);
    }

    [Fact]
    public void Flat_settings_report_no_adjustment_and_carry_the_specs_caveat()
    {
        var pace = PaceModel.Adjust(BarcelonaRacing(), PaceSettings.Flat);

        Assert.False(pace.IsAdjusted);
        Assert.Empty(pace.Steps);
        Assert.Equal(1.0, pace.PaceMultiplier, 12);
        Assert.Equal(PoissonGoalModel.PaceCaveat, PaceModel.CaveatFor(pace));
    }

    [Fact]
    public void Flat_settings_still_warn_about_an_impossible_projection()
    {
        // Turning the correction off does not make the projection sane; it just means nothing is
        // being done about it, and the warning says exactly that.
        var pace = PaceModel.Adjust(BarcelonaRacing(), PaceSettings.Flat);

        Assert.NotNull(pace.Warning);
        Assert.Contains("switched OFF", pace.Warning);
    }

    [Fact]
    public void Pre_match_and_zero_minute_states_are_refused_by_the_pace_model_too()
    {
        Assert.Throws<InvalidOperationException>(
            () => PaceModel.Adjust(new LiveMatchState(0, 0, 0, 0, 0, IsPreMatch: true), PaceSettings.Default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PaceModel.Adjust(new LiveMatchState(0, 0.5, 0.5, 0, 0, false), PaceSettings.Default));
    }

    // Each of these switches on one correction and nothing else, so a test of it measures that
    // correction alone — stoppage changes the minutes every rate is measured in, and the tempo what
    // each of them is worth.
    private static PaceSettings Shrinkage() => PaceSettings.Flat with { Shrink = true };

    private static PaceSettings ScoreState() => PaceSettings.Flat with { ScoreState = true };

    /// <summary>The two corrections the Barcelona case was first fixed with, on the spec's clock.</summary>
    private static PaceSettings Rates() => PaceSettings.Flat with { Shrink = true, ScoreState = true };
}
