using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class AnalysisServiceTests
{
    private readonly AnalysisService _service = new();

    private static LiveMatchState Live(int minute, double xgHome, double xgAway, int goalsHome = 0, int goalsAway = 0)
        => new(minute, xgHome, xgAway, goalsHome, goalsAway, IsPreMatch: false);

    [Fact]
    public void A_live_analysis_de_vigs_the_market_and_prices_the_outcome_from_xg()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.57, [1.66]),
            Live(67, 0.79, 0.01, 1, 0),
            Bankroll: 263.0,
            Pace: PaceSettings.Flat));

        Assert.Equal(0.5139, response.Edge.FairMarketProbability, 4);
        Assert.Equal("poisson-live-xg", response.Estimate.Method);
        Assert.Equal(1.2394, response.Overround, 4);
        Assert.NotNull(response.Model);
        Assert.Equal(23, response.Model!.RemainingMinutes);
        Assert.Equal("Under 4", response.Model.ResolvedSelection);
    }

    [Fact]
    public void The_edge_is_the_gap_between_our_number_and_the_de_vigged_one()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.BothTeamsToScore, "Da", 1.80, [1.95]),
            LiveState: null,
            Bankroll: 263.0,
            Selection: MarketSelection.Yes,
            ManualOwnProbability: 0.62));

        var expectedFair = OddsMath.DeVig(1.80, [1.95]);

        Assert.Equal(expectedFair, response.Edge.FairMarketProbability, 10);
        Assert.Equal(0.62, response.Edge.OwnProbability, 10);
        Assert.Equal((0.62 - expectedFair) * 100.0, response.Edge.EdgePercentagePoints, 10);
        Assert.Equal(0.62 * 1.80 - 1.0, response.Edge.ExpectedValuePerUnit, 10);
    }

    [Fact]
    public void A_pre_match_analysis_uses_the_supplied_number_and_runs_no_model()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.Moneyline1X2, "Sevilla FC", 2.40, [3.30, 3.10]),
            LiveState: null,
            Bankroll: 263.0,
            Selection: MarketSelection.Home,
            ManualOwnProbability: 0.47));

        Assert.Equal("manual-input", response.Estimate.Method);
        Assert.Equal(0.47, response.Edge.OwnProbability, 10);
        Assert.Null(response.Model);
        Assert.DoesNotContain(PoissonGoalModel.PaceCaveat, response.VerdictReason);
    }

    [Fact]
    public void A_state_flagged_pre_match_is_treated_as_pre_match_even_when_live_fields_are_filled_in()
    {
        // IsPreMatch wins over everything else in the record, exactly as the domain model says.
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 1.90, [1.90]),
            new LiveMatchState(67, 0.79, 0.01, 1, 0, IsPreMatch: true),
            Bankroll: 100.0,
            ManualOwnProbability: 0.55));

        Assert.Equal("manual-input", response.Estimate.Method);
        Assert.Null(response.Model);
    }

    [Fact]
    public void Pre_match_without_a_probability_is_refused_instead_of_guessed()
    {
        var ex = Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 1.90, [1.90]),
            LiveState: null,
            Bankroll: 263.0)));

        Assert.Contains("manualOwnProbability", ex.Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.4)]
    public void A_manual_probability_outside_zero_to_one_is_refused(double probability)
        => Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 1.90, [1.90]),
            LiveState: null,
            Bankroll: 263.0,
            ManualOwnProbability: probability)));

    [Fact]
    public void An_explicit_selection_overrides_whatever_the_label_happens_to_say()
    {
        // The UI always sends the selection, so a label the parser would read differently must not win.
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.NextGoal, "Primul gol: 2", 1.60, [3.20, 4.50]),
            Live(45, 0.13, 0.86),
            Bankroll: 263.0,
            Selection: MarketSelection.NextGoalHome,
            Pace: PaceSettings.Flat));

        Assert.Equal("NextGoalHome", response.Model!.ResolvedSelection);
        Assert.Equal(0.0825, response.Edge.OwnProbability, 4);
    }

    [Fact]
    public void The_label_is_parsed_when_no_selection_is_supplied()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.NextGoal, "Primul gol: 2", 1.60, [3.20, 4.50]),
            Live(45, 0.13, 0.86),
            Bankroll: 263.0));

        Assert.Equal("NextGoalAway", response.Model!.ResolvedSelection);
    }

    [Fact]
    public void A_skip_verdict_zeroes_the_stake_and_says_why()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.NextGoal, "Primul gol: 2", 1.60, [3.20, 4.50]),
            Live(45, 0.13, 0.86),
            Bankroll: 263.0,
            Selection: MarketSelection.NextGoalAway));

        Assert.Equal(Verdict.Skip, response.Verdict);
        Assert.Equal(0.0, response.Stake.SuggestedStakeAmount);
        Assert.Equal("skip-below-threshold", response.Stake.CapReason);
    }

    [Fact]
    public void A_real_edge_produces_a_real_stake_inside_the_band()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.BothTeamsToScore, "Da", 2.10, [1.85]),
            LiveState: null,
            Bankroll: 1000.0,
            Selection: MarketSelection.Yes,
            ManualOwnProbability: 0.58));

        Assert.Equal(Verdict.Bet, response.Verdict);
        Assert.InRange(
            response.Stake.SuggestedStakeFraction,
            KellyStaking.MinimumStakeFraction,
            KellyStaking.MaximumStakeFraction);
        Assert.Equal(response.Stake.SuggestedStakeFraction * 1000.0, response.Stake.SuggestedStakeAmount, 10);
    }

    [Fact]
    public void Every_live_estimate_carries_a_pace_caveat_into_the_response()
    {
        // The wording depends on which way the pace model corrected, but SOME caveat always travels
        // with a live number, into the notes and into the verdict reason both.
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 2.30, [1.65]),
            Live(55, 1.1, 0.9, 1, 0),
            Bankroll: 263.0));

        var caveat = PaceModel.CaveatFor(response.Model!.Pace);

        Assert.Contains(caveat, response.Estimate.Notes);
        Assert.Contains(caveat, response.VerdictReason);
    }

    [Fact]
    public void The_spec_caveat_is_what_a_flat_analysis_still_carries()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 2.30, [1.65]),
            Live(55, 1.1, 0.9, 1, 0),
            Bankroll: 263.0,
            Pace: PaceSettings.Flat));

        Assert.Contains(PoissonGoalModel.PaceCaveat, response.Estimate.Notes);
        Assert.Contains(PoissonGoalModel.PaceCaveat, response.VerdictReason);
    }

    [Fact]
    public void Every_response_carries_the_disclaimer_so_it_survives_direct_API_use()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 2.30, [1.65]),
            Live(55, 1.1, 0.9, 1, 0),
            Bankroll: 263.0));

        Assert.Equal("Educational decision-support only. Not financial or betting advice.", response.Disclaimer);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void Impossible_prices_are_refused_with_a_message_the_form_can_show(double odds)
    {
        var ex = Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", odds, [1.66]),
            Live(67, 0.79, 0.01, 1, 0),
            Bankroll: 263.0)));

        Assert.Contains("greater than 1.0", ex.Message);
    }

    [Fact]
    public void A_market_with_no_siblings_is_refused_because_the_margin_is_unknowable()
    {
        var ex = Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.57, []),
            Live(67, 0.79, 0.01, 1, 0),
            Bankroll: 263.0)));

        Assert.Contains("sibling", ex.Message);
    }

    [Fact]
    public void A_negative_bankroll_is_refused()
        => Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.57, [1.66]),
            Live(67, 0.79, 0.01, 1, 0),
            Bankroll: -10.0)));

    [Fact]
    public void A_broken_live_state_surfaces_as_an_input_error_not_a_five_hundred()
    {
        var ex = Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.57, [1.66]),
            Live(0, 0.79, 0.01),
            Bankroll: 263.0)));

        Assert.Contains("Minute played", ex.Message);
    }

    [Fact]
    public void An_unreadable_label_surfaces_as_an_input_error_not_a_five_hundred()
        => Assert.Throws<AnalysisInputException>(() => _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.Moneyline1X2, "Sevilla FC", 2.40, [3.30, 3.10]),
            Live(45, 0.5, 0.5),
            Bankroll: 263.0)));

    [Fact]
    public void A_line_supplied_separately_fills_in_a_label_that_did_not_carry_one()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Total goals", 1.90, [1.90]),
            Live(45, 0.9, 0.8),
            Bankroll: 263.0,
            Selection: MarketSelection.Over,
            Line: 2.5));

        Assert.Equal("Over 2.5", response.Model!.ResolvedSelection);
    }
}
