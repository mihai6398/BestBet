using BestBet.Contracts;
using BestBet.Data;
using BestBet.Data.Entities;
using BestBet.Domain;
using BestBet.Services;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// Correcting a saved row to what was actually bet, and asking what it is worth now.
/// </summary>
public class PositionEditingTests
{
    private static BestBetDbContext NewDb()
        => new(new DbContextOptionsBuilder<BestBetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>Over 2.5 taken at 2.30 against 1.60, with the model at 52.3% on a 100 bankroll.</summary>
    private static SavePositionRequest SampleSave() => new(
        MatchLabel: "Sunderland - Arsenal",
        MarketKind: MarketKind.TotalGoals,
        MarketLabel: "Peste 2.5",
        Selection: "Over 2.5",
        EntryDecimalOdds: 2.30,
        EntrySiblingOdds: [1.60],
        EntryFairProbability: OddsMath.DeVig(2.30, [1.60]),
        EntryOwnProbability: 0.523,
        EntryEdgePercentagePoints: (0.523 - OddsMath.DeVig(2.30, [1.60])) * 100.0,
        EstimateMethod: "poisson-live-xg-paced",
        BankrollAtEntry: 100.0,
        StakeAmount: 3.88,
        StakeFraction: 0.0388,
        Verdict: Verdict.Bet,
        WasLive: true,
        MinutePlayed: 67,
        CumulativeXgHome: 1.35,
        CumulativeXgAway: 0.90,
        GoalsHome: 1,
        GoalsAway: 1,
        Notes: null);

    // --- Correcting what was taken ------------------------------------------

    [Fact]
    public async Task The_price_taken_can_be_corrected_after_the_fact()
    {
        // The reason this exists: a live price moves between reading it and clicking it, and the row
        // has to record the fill rather than the quote, or every CLV number measures the wrong bet.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(saved.Id, new UpdatePositionRequest(EntryDecimalOdds: 2.15));

        Assert.Equal(2.15, updated!.EntryDecimalOdds);
    }

    [Fact]
    public async Task Correcting_the_price_re_derives_the_fair_line_and_the_edge_from_it()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(saved.Id, new UpdatePositionRequest(EntryDecimalOdds: 2.15));

        // Both are functions of the price. Keeping the old ones would describe a bet at 2.30 with
        // the edge of a bet at 2.15, and that row feeds the scoreboard.
        Assert.Equal(OddsMath.DeVig(2.15, [1.60]), updated!.EntryFairProbability, 10);
        Assert.Equal((0.523 - updated.EntryFairProbability) * 100.0, updated.EntryEdgePercentagePoints, 10);
        Assert.True(updated.EntryEdgePercentagePoints < saved.EntryEdgePercentagePoints);
    }

    [Fact]
    public async Task The_model_s_own_probability_survives_a_price_correction()
    {
        // Getting a worse price says nothing about what the match was doing.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(saved.Id, new UpdatePositionRequest(EntryDecimalOdds: 2.15));

        Assert.Equal(0.523, updated!.EntryOwnProbability, 10);
        Assert.Equal(saved.MinutePlayed, updated.MinutePlayed);
        Assert.Equal(saved.CumulativeXgHome, updated.CumulativeXgHome);
    }

    [Fact]
    public async Task The_other_prices_in_the_market_can_be_corrected_too()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(
            saved.Id,
            new UpdatePositionRequest(EntrySiblingOdds: [1.70]));

        Assert.Equal([1.70], updated!.EntrySiblingOdds);
        Assert.Equal(OddsMath.DeVig(2.30, [1.70]), updated.EntryFairProbability, 10);
    }

    [Fact]
    public async Task The_stake_can_be_corrected_and_its_fraction_follows()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(saved.Id, new UpdatePositionRequest(StakeAmount: 5.0));

        Assert.Equal(5.0, updated!.StakeAmount);
        Assert.Equal(0.05, updated.StakeFraction, 10);
    }

    [Fact]
    public async Task A_corrected_stake_moves_the_settled_profit_with_it()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        await service.UpdateAsync(saved.Id, new UpdatePositionRequest(Outcome: PositionOutcome.Won));
        var updated = await service.UpdateAsync(saved.Id, new UpdatePositionRequest(StakeAmount: 10.0));

        Assert.Equal(10.0 * (2.30 - 1.0), updated!.ProfitLoss!.Value, 10);
    }

    [Fact]
    public async Task A_price_at_or_below_evens_is_refused_rather_than_stored()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        await Assert.ThrowsAsync<AnalysisInputException>(
            () => service.UpdateAsync(saved.Id, new UpdatePositionRequest(EntryDecimalOdds: 0.95)));
    }

    [Fact]
    public async Task A_negative_stake_is_refused()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        await Assert.ThrowsAsync<AnalysisInputException>(
            () => service.UpdateAsync(saved.Id, new UpdatePositionRequest(StakeAmount: -1.0)));
    }

    [Fact]
    public async Task An_update_that_touches_neither_leaves_the_entry_exactly_as_it_was()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(saved.Id, new UpdatePositionRequest(ClosingDecimalOdds: 2.10));

        Assert.Equal(saved.EntryDecimalOdds, updated!.EntryDecimalOdds);
        Assert.Equal(saved.EntryFairProbability, updated.EntryFairProbability, 10);
        Assert.Equal(saved.EntryEdgePercentagePoints, updated.EntryEdgePercentagePoints, 10);
        Assert.Equal(saved.StakeAmount, updated.StakeAmount);
    }

    // --- Asking what it is worth now ----------------------------------------

    private static RepricePositionRequest AtMinute(int minute, double xgHome, double xgAway, int home, int away)
        => new(new LiveMatchState(minute, xgHome, xgAway, home, away, false));

    [Fact]
    public async Task An_open_position_can_be_priced_again_against_the_match_as_it_is_now()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var now = await service.RepriceAsync(saved.Id, AtMinute(80, 1.60, 1.10, 1, 1));

        Assert.NotNull(now);
        Assert.Equal(saved.Id, now!.Id);
        Assert.Equal("Over 2.5", now.Selection);
        Assert.InRange(now.OwnProbability, 0.0, 1.0);
    }

    [Fact]
    public async Task Re_pricing_changes_nothing_on_the_row()
    {
        // The entry snapshot is the evidence that the decision was reasonable when it was made.
        // Overwriting it with a later reading would destroy the only thing the log is for.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        await service.RepriceAsync(saved.Id, AtMinute(80, 1.60, 1.10, 1, 1));
        var row = (await service.ListAsync()).Single();

        Assert.Equal(67, row.MinutePlayed);
        Assert.Equal(1.35, row.CumulativeXgHome);
        Assert.Equal(0.523, row.EntryOwnProbability, 10);
    }

    [Fact]
    public async Task A_goal_against_an_over_bet_makes_it_more_likely_to_win()
    {
        // Over 2.5 with two goals in needs one more; with three already in, it has won. The direction
        // of this is the whole reason to re-price: the bet is a different bet than it was.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var before = await service.RepriceAsync(saved.Id, AtMinute(80, 1.60, 1.10, 1, 1));
        var after = await service.RepriceAsync(saved.Id, AtMinute(80, 1.90, 1.10, 2, 1));

        Assert.True(after!.OwnProbability > before!.OwnProbability);
        Assert.True(after.ProbabilityPoints > before.ProbabilityPoints);
    }

    [Fact]
    public async Task Re_pricing_uses_the_pre_match_numbers_when_it_is_given_them()
    {
        // An open bet has to be re-priced on the same terms the analysis priced it on. If the
        // analysis knew these were two attacking sides, the re-price must not forget it.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());
        var state = new LiveMatchState(60, 0.90, 0.80, 1, 1, false);

        var league = await service.RepriceAsync(saved.Id, new RepricePositionRequest(state));
        var attacking = await service.RepriceAsync(
            saved.Id,
            new RepricePositionRequest(state, PreMatch: new PreMatchRates(2.40, 1.90)));

        Assert.True(attacking!.OwnProbability > league!.OwnProbability);
        Assert.Equal(AnalysisService.LivePreMatchPriorMethod, attacking.Estimate.Method);
    }

    [Fact]
    public async Task The_EV_is_measured_at_the_price_already_taken()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var now = await service.RepriceAsync(saved.Id, AtMinute(80, 1.60, 1.10, 1, 1));

        Assert.Equal(OddsMath.ExpectedValuePerUnit(now!.OwnProbability, 2.30), now.ExpectedValuePerUnit, 10);
        Assert.Equal(now.ExpectedValuePerUnit * 3.88, now.ExpectedValueAmount, 10);
    }

    [Fact]
    public async Task The_fair_price_now_is_the_number_to_judge_a_cash_out_against()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var now = await service.RepriceAsync(saved.Id, AtMinute(80, 1.60, 1.10, 1, 1));

        Assert.Equal(1.0 / now!.OwnProbability, now.FairOddsNow!.Value, 10);
    }

    [Fact]
    public async Task A_bet_that_can_no_longer_win_has_no_fair_price_rather_than_an_infinite_one()
    {
        // Under 2.5 with three goals already scored is not a long shot, it is over. 1/0 is not a
        // price, and the infinity it used to produce could not be serialised at all — so the whole
        // answer disappeared at exactly the moment it mattered, and a monitor watching the match
        // kept showing the numbers from before the goals went in.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave() with { Selection = "Under 2.5" });

        var now = await service.RepriceAsync(saved.Id, AtMinute(80, 2.40, 1.60, 2, 1));

        Assert.Equal(0.0, now!.OwnProbability, 6);
        Assert.Null(now.FairOddsNow);
        Assert.Equal(-1.0, now.ExpectedValuePerUnit, 6);
    }

    [Fact]
    public async Task A_bet_that_has_already_won_still_gets_a_price()
    {
        // The other end of the same line: Over 2.5 with three goals in is already won, and 1.00 is
        // a real price.
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var now = await service.RepriceAsync(saved.Id, AtMinute(88, 2.40, 1.60, 2, 1));

        Assert.Equal(1.0, now!.OwnProbability, 6);
        Assert.Equal(1.0, now.FairOddsNow!.Value, 6);
    }

    [Fact]
    public async Task Re_pricing_a_row_that_is_not_there_says_so_rather_than_throwing()
    {
        using var db = NewDb();
        var service = new PositionService(db);

        Assert.Null(await service.RepriceAsync(404, AtMinute(80, 1.60, 1.10, 1, 1)));
    }

    [Fact]
    public async Task Re_pricing_needs_a_match_in_progress()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave());

        var preMatch = new RepricePositionRequest(new LiveMatchState(0, 0, 0, 0, 0, true));

        await Assert.ThrowsAsync<AnalysisInputException>(() => service.RepriceAsync(saved.Id, preMatch));
    }

    [Fact]
    public async Task A_row_whose_selection_cannot_be_read_back_says_which_row_and_why()
    {
        using var db = NewDb();
        var service = new PositionService(db);
        var saved = await service.SaveAsync(SampleSave() with { Selection = "something nobody can parse" });

        var error = await Assert.ThrowsAsync<AnalysisInputException>(
            () => service.RepriceAsync(saved.Id, AtMinute(80, 1.60, 1.10, 1, 1)));

        Assert.Contains("something nobody can parse", error.Message);
    }
}

/// <summary>Reading a stored selection back into a side of a market.</summary>
public class StoredSelectionTests
{
    [Fact]
    public void Reads_the_canonical_form_this_app_writes()
    {
        var spec = StoredSelection.Parse(MarketKind.TotalGoals, "Over 2.5");

        Assert.Equal(MarketSelection.Over, spec.Side);
        Assert.Equal(2.5, spec.Line);
    }

    [Fact]
    public void Reads_a_side_that_has_no_line()
    {
        var spec = StoredSelection.Parse(MarketKind.NextGoal, "NextGoalAway");

        Assert.Equal(MarketSelection.NextGoalAway, spec.Side);
        Assert.Null(spec.Line);
    }

    [Fact]
    public void Reads_a_whole_number_line()
        => Assert.Equal(4.0, StoredSelection.Parse(MarketKind.TotalGoals, "Under 4").Line);

    [Fact]
    public void Reads_the_line_the_same_way_on_every_machine()
    {
        // Written with InvariantCulture, so it has to be read with one too. A machine with a comma
        // decimal separator must not turn "Over 2.5" into the 25th goal line.
        var spec = StoredSelection.Parse(MarketKind.TotalGoals, "Over 2.5");
        Assert.Equal(2.5, spec.Line);
    }

    [Fact]
    public void Falls_back_to_the_free_text_parser_for_rows_written_before_the_canonical_form()
        => Assert.Equal(MarketSelection.Under, StoredSelection.Parse(MarketKind.TotalGoals, "Sub 3.5").Side);

    [Fact]
    public void Refuses_a_selection_it_cannot_read_rather_than_guessing_a_side()
    {
        // Guessing here would invent which side of a market a stake that is already down is on.
        var error = Assert.Throws<AnalysisInputException>(
            () => StoredSelection.Parse(MarketKind.TotalGoals, "mystery"));

        Assert.Contains("mystery", error.Message);
    }

    [Fact]
    public void Refuses_an_empty_selection()
        => Assert.Throws<AnalysisInputException>(() => StoredSelection.Parse(MarketKind.TotalGoals, "  "));
}
