using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// When to take a live price, on the bets that prompted the question and on the cases the plan has
/// to get right in both directions.
/// </summary>
public class TimingAdvisorTests
{
    private readonly AnalysisService _service = new();

    private static readonly PreMatchRates Prior = new(1.5, 1.2);

    private static AnalyzeRequest Total(
        string label,
        double odds,
        double sibling,
        MarketSelection side,
        double line,
        LiveMatchState state,
        PreMatchRates? preMatch = null,
        PaceSettings? pace = null,
        double? age = null) => new(
        new MarketOdds(MarketKind.TotalGoals, label, odds, [sibling]),
        state,
        Bankroll: 263.0,
        Selection: side,
        Line: line,
        Pace: pace,
        PreMatch: preMatch,
        FeedAgeSeconds: age);

    /// <summary>PSG–Barcelona, Under 3 at minute 22, 1-0 on 0.15 xG: saved as a 34-point edge, and lost.</summary>
    private static AnalyzeRequest LostUnder() => Total(
        "Sub 3", 2.38, 1.47, MarketSelection.Under, 3, new LiveMatchState(22, 0.08, 0.07, 1, 0, false));

    /// <summary>0-0 at minute 23 on half a goal of xG, Under 3.5 at 1.22.</summary>
    private static AnalyzeRequest EarlyUnder(PreMatchRates? preMatch = null, double? age = null) => Total(
        "Sub 3.5", 1.22, 4.20, MarketSelection.Under, 3.5, new LiveMatchState(23, 0.30, 0.20, 0, 0, false), preMatch, age: age);

    // --- When there is nothing to plan ---------------------------------------

    [Fact]
    public void A_pre_match_analysis_has_no_timing()
    {
        var response = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 1.90, [1.90]),
            new LiveMatchState(0, 0, 0, 0, 0, IsPreMatch: true),
            Bankroll: 100.0,
            Selection: MarketSelection.Over,
            Line: 2.5,
            ManualOwnProbability: 0.55));

        Assert.Null(response.Timing);
    }

    [Fact]
    public void A_price_the_market_and_the_model_agree_on_is_worth_nothing_at_any_moment()
    {
        // Priced at the model's own number with no margin: whoever is believed, there is no value to
        // take now, and none for waiting to find.
        var state = new LiveMatchState(60, 0.9, 0.8, 1, 0, false);
        var own = _service.Analyze(Total("Sub 2.5", 1.90, 1.90, MarketSelection.Under, 2.5, state, Prior)).Edge.OwnProbability;

        var response = _service.Analyze(Total("Sub 2.5", 1 / own, 1 / (1 - own), MarketSelection.Under, 2.5, state, Prior));
        var timing = response.Timing!;

        Assert.Equal(TimingAction.Skip, timing.Action);
        Assert.Equal(timing.ModelRemainingGoals, timing.MarketRemainingGoals!.Value, 6);
    }

    // --- The bets that prompted it -------------------------------------------

    [Fact]
    public void The_lost_under_is_told_to_wait_and_why()
    {
        var timing = _service.Analyze(LostUnder()).Timing!;

        Assert.Equal(TimingAction.Wait, timing.Action);
        Assert.Contains(timing.Reasons, r => r.Code == TimingAdvisor.EdgeTooLarge);
        Assert.Contains(timing.Reasons, r => r.Code == TimingAdvisor.EarlyLeaguePrior);
        Assert.True(timing.LeaguePrior);

        // The market was pricing in about a goal more than the model, which is the gap a
        // league-average prior opens on PSG–Barcelona.
        Assert.True(timing.MarketRemainingGoals > timing.ModelRemainingGoals + 0.7);
    }

    [Fact]
    public void An_early_under_on_a_league_average_is_not_worth_it_now_or_soon()
    {
        // A 6-point edge on the model's number is 2 points once the model is only believed as far as
        // 23 minutes of evidence go, and 2 points do not cover the margin in 1.22 / 4.20.
        var response = _service.Analyze(EarlyUnder());
        var timing = response.Timing!;

        Assert.Equal(Verdict.Bet, response.Verdict);
        Assert.Equal(TimingAction.Skip, timing.Action);
        Assert.Equal(0.31, timing.Credibility, 2);

        var early = Assert.Single(timing.Reasons, r => r.Code == TimingAdvisor.EarlyLeaguePrior);
        Assert.Equal(34, early.Reference);
    }

    [Fact]
    public void Pre_match_numbers_take_away_the_early_warning()
    {
        var timing = _service.Analyze(EarlyUnder(Prior)).Timing!;

        Assert.DoesNotContain(timing.Reasons, r => r.Code == TimingAdvisor.EarlyLeaguePrior);
        Assert.False(timing.LeaguePrior);
    }

    [Fact]
    public void A_stale_reading_is_believed_half_as_much_and_said_out_loud()
    {
        var fresh = _service.Analyze(EarlyUnder(age: 10)).Timing!;
        var stale = _service.Analyze(EarlyUnder(age: 95)).Timing!;

        Assert.Equal(fresh.Credibility * TimingAdvisor.StaleCredibilityFactor, stale.Credibility, 10);
        Assert.DoesNotContain(fresh.Reasons, r => r.Code == TimingAdvisor.StaleData);
        Assert.Equal(95, Assert.Single(stale.Reasons, r => r.Code == TimingAdvisor.StaleData).Value);
    }

    // --- Both directions ------------------------------------------------------

    [Fact]
    public void An_over_on_a_quiet_match_waits_for_the_price_to_drift()
    {
        // 0-0 at 55 with the model expecting more goals than the market: every quiet minute lengthens
        // the Over, and the model is believed a little more for each one — so later is worth more.
        var timing = _service.Analyze(Total(
            "Peste 0.5", 1.45, 2.65, MarketSelection.Over, 0.5, new LiveMatchState(55, 1.1, 0.9, 0, 0, false), Prior)).Timing!;

        Assert.Empty(timing.Reasons);
        Assert.Equal(TimingAction.Wait, timing.Action);
        Assert.Equal(15, timing.WaitMinutes);

        var later = timing.Options.Single(o => o.MinutesFromNow == 15);
        Assert.True(later.ExpectedOdds > 1.45);
        Assert.True(later.ExpectedValue > timing.Options[0].ExpectedValue);
    }

    [Fact]
    public void A_credible_edge_with_nothing_to_wait_for_is_taken_now()
    {
        var timing = _service.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.Moneyline1X2, "1", 3.10, [2.10, 4.50]),
            new LiveMatchState(65, 1.4, 0.4, 0, 0, false),
            Bankroll: 263.0,
            Selection: MarketSelection.Home,
            PreMatch: Prior)).Timing!;

        Assert.Equal(TimingAction.BetNow, timing.Action);
        Assert.Null(timing.WaitMinutes);

        // A result market has no single goal count to read out of its price.
        Assert.Null(timing.MarketRemainingGoals);
    }

    [Fact]
    public void A_hot_start_holds_up_an_over_but_not_an_under()
    {
        var state = new LiveMatchState(36, 2.38, 0.30, 3, 1, false);

        var over = _service.Analyze(Total("Peste 6.5", 1.91, 1.91, MarketSelection.Over, 6.5, state)).Timing!;
        var under = _service.Analyze(Total("Sub 6.5", 1.91, 1.91, MarketSelection.Under, 6.5, state)).Timing!;

        Assert.Contains(over.Reasons, r => r.Code == TimingAdvisor.HotStart);
        Assert.DoesNotContain(under.Reasons, r => r.Code == TimingAdvisor.HotStart);
    }

    [Fact]
    public void A_match_opening_up_holds_up_an_under()
    {
        // 0.5 xG in the first 40 minutes, then 1.1 in the last twenty.
        var pace = new PaceSettings(RecentWindowMinutes: 20, RecentXgHome: 0.6, RecentXgAway: 0.5);
        var timing = _service.Analyze(Total(
            "Sub 2.5", 1.60, 2.30, MarketSelection.Under, 2.5, new LiveMatchState(60, 0.9, 0.7, 0, 0, false), pace: pace)).Timing!;

        var heating = Assert.Single(timing.Reasons, r => r.Code == TimingAdvisor.HeatingUp);
        Assert.True(heating.Value > heating.Reference * TimingAdvisor.MomentumRatio);
    }

    [Fact]
    public void A_match_dying_holds_up_an_over()
    {
        var pace = new PaceSettings(RecentWindowMinutes: 20, RecentXgHome: 0.05, RecentXgAway: 0.05);
        var timing = _service.Analyze(Total(
            "Peste 2.5", 2.40, 1.55, MarketSelection.Over, 2.5, new LiveMatchState(60, 1.2, 0.9, 1, 0, false), pace: pace)).Timing!;

        Assert.Contains(timing.Reasons, r => r.Code == TimingAdvisor.CoolingDown);
    }

    // --- The plan's working --------------------------------------------------

    [Fact]
    public void Each_later_moment_accounts_for_everything_that_can_happen()
    {
        var timing = _service.Analyze(EarlyUnder()).Timing!;

        Assert.Equal([0, 5, 10, 15], timing.Options.Select(o => o.MinutesFromNow));
        foreach (var option in timing.Options.Skip(1))
        {
            Assert.Equal(1.0, option.Scenarios.Sum(s => s.Probability), 9);
            Assert.Equal("no-goal", option.Scenarios[0].Code);
        }
    }

    [Fact]
    public void A_quiet_spell_raises_an_under_and_a_goal_sinks_it()
    {
        var later = _service.Analyze(EarlyUnder()).Timing!.Options.Single(o => o.MinutesFromNow == 10);
        var quiet = later.Scenarios.Single(s => s.Code == "no-goal");
        var goal = later.Scenarios.Single(s => s.Code == "home-goal");

        Assert.True(quiet.ModelProbability > 0.838);
        Assert.True(goal.ModelProbability < 0.75);
        Assert.True(quiet.Odds < 1.22);
        Assert.True(goal.Odds > 1.22);

        // The model expected fewer goals than the market: quiet minutes are evidence for it, a goal against.
        Assert.True(quiet.Credibility > goal.Credibility);
    }

    [Fact]
    public void A_later_moment_crosses_half_time_on_the_clock_of_play()
    {
        // Minute 40, ten minutes of play on: five to the whistle, three of stoppage, two of the second half.
        var timing = _service.Analyze(Total(
            "Sub 3.5", 1.30, 3.40, MarketSelection.Under, 3.5, new LiveMatchState(40, 0.6, 0.4, 1, 0, false), Prior)).Timing!;

        Assert.Equal(47, timing.Options.Single(o => o.MinutesFromNow == 10).AtMinute);
    }

    [Fact]
    public void Near_the_end_only_now_is_left_to_plan()
    {
        var timing = _service.Analyze(Total(
            "Sub 3.5", 1.10, 6.0, MarketSelection.Under, 3.5, new LiveMatchState(85, 1.0, 0.8, 1, 1, false))).Timing!;

        Assert.Equal(0, Assert.Single(timing.Options).MinutesFromNow);
    }

    [Fact]
    public void Credibility_is_the_share_of_the_estimate_built_from_the_match()
    {
        var early = PaceModel.Adjust(new LiveMatchState(20, 0.4, 0.3, 0, 0, false), PaceSettings.Default);
        var late = PaceModel.Adjust(new LiveMatchState(80, 1.4, 1.1, 0, 0, false), PaceSettings.Default);

        Assert.Equal(early.EvidenceWeight, TimingAdvisor.Credibility(early, PaceSettings.Default), 12);
        Assert.True(TimingAdvisor.Credibility(late, PaceSettings.Default) > 0.6);

        // Switching the prior off does not make twenty minutes prove more.
        var raw = PaceModel.Adjust(new LiveMatchState(20, 0.4, 0.3, 0, 0, false), PaceSettings.Flat);
        Assert.Equal(1.0, raw.EvidenceWeight);
        Assert.True(TimingAdvisor.Credibility(raw, PaceSettings.Flat) < 0.35);
    }

    [Fact]
    public void The_verdict_and_the_stake_are_left_exactly_as_they_were()
    {
        var with = _service.Analyze(LostUnder());
        var stale = _service.Analyze(LostUnder() with { FeedAgeSeconds = 300 });

        Assert.Equal(with.Verdict, stale.Verdict);
        Assert.Equal(with.Stake, stale.Stake);
        Assert.Equal(with.Edge, stale.Edge);
    }
}
