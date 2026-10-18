using BestBet.Contracts;
using BestBet.Data;
using BestBet.Data.Entities;
using BestBet.Domain;
using BestBet.Services;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Server.Tests.Services;

public class ClvMathTests
{
    [Fact]
    public void Taking_a_longer_price_than_the_close_is_positive_CLV()
    {
        // 2.10 on a line that closed at 2.00: the same bet was worth 5% more when it was struck.
        Assert.Equal(0.05, ClvMath.ClvPercent(2.10, 2.00)!.Value, 10);
        Assert.True(ClvMath.BeatTheClose(2.10, 2.00));
    }

    [Fact]
    public void Taking_a_shorter_price_than_the_close_is_negative_CLV()
    {
        Assert.True(ClvMath.ClvPercent(1.90, 2.00) < 0);
        Assert.False(ClvMath.BeatTheClose(1.90, 2.00));
    }

    [Fact]
    public void Matching_the_close_exactly_is_neither()
        => Assert.False(ClvMath.BeatTheClose(2.00, 2.00));

    [Fact]
    public void CLV_is_null_while_the_closing_price_is_unknown()
    {
        Assert.Null(ClvMath.ClvPercent(2.10, null));
        Assert.Null(ClvMath.BeatTheClose(2.10, null));
    }

    [Fact]
    public void CLV_in_probability_points_de_vigs_the_close_the_same_way_the_entry_was()
    {
        // Entered at a fair 51.4%; the market closed at 1.40 / 3.20, which de-vigs to 69.6%. The line
        // moved 18 points towards the bet.
        var entryFair = OddsMath.DeVig(1.57, [1.66]);
        var points = ClvMath.ClvProbabilityPoints(entryFair, 1.40, [3.20]);

        Assert.NotNull(points);
        Assert.Equal((OddsMath.DeVig(1.40, [3.20]) - entryFair) * 100.0, points!.Value, 10);
        Assert.True(points > 0);
    }

    [Fact]
    public void CLV_in_probability_points_needs_the_closing_siblings_and_says_null_without_them()
        => Assert.Null(ClvMath.ClvProbabilityPoints(0.5139, 1.40, []));

    [Theory]
    [InlineData("1.66", new[] { 1.66 })]
    [InlineData("1.66, 2.10", new[] { 1.66, 2.10 })]
    [InlineData(" 3.2 ,4.5 ", new[] { 3.2, 4.5 })]
    public void The_sibling_odds_column_round_trips(string csv, double[] expected)
        => Assert.Equal(expected, ClvMath.ParseOddsCsv(csv));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    public void A_blank_or_broken_sibling_column_parses_to_nothing_rather_than_throwing(string? csv)
    {
        // This column is typed in by hand, so it has to survive whatever ends up in it.
        Assert.Empty(ClvMath.ParseOddsCsv(csv));
    }

    [Fact]
    public void Formatting_and_parsing_are_inverses()
    {
        double[] odds = [1.57, 3.2, 12.0];
        Assert.Equal(odds, ClvMath.ParseOddsCsv(ClvMath.FormatOddsCsv(odds)));
    }
}

public class BetServiceSummaryTests
{
    private static BetDto Position(
        double entryOdds,
        double? closingOdds,
        BetStatus outcome = BetStatus.Open,
        double stake = 10.0)
        => new(
            Id: 0,
            CreatedUtc: DateTime.UtcNow,
            MatchLabel: "A - B",
            MarketKind: MarketKind.TotalGoals,
            MarketLabel: "Peste 2.5",
            Selection: "Over 2.5",
            EntryDecimalOdds: entryOdds,
            EntrySiblingOdds: [1.90],
            EntryFairProbability: 0.5,
            EntryOwnProbability: 0.55,
            EntryEdgePercentagePoints: 5.0,
            EstimateMethod: "poisson-live-xg",
            BankrollAtEntry: 263.0,
            StakeAmount: stake,
            StakeFraction: 0.04,
            Verdict: Verdict.Bet,
            WasLive: true,
            MinutePlayed: 45,
            CumulativeXgHome: 0.5,
            CumulativeXgAway: 0.4,
            GoalsHome: 0,
            GoalsAway: 0,
            ClosingDecimalOdds: closingOdds,
            ClosingSiblingOdds: [],
            Status: outcome,
            Notes: null,
            ClvPercent: ClvMath.ClvPercent(entryOdds, closingOdds),
            BeatTheClose: ClvMath.BeatTheClose(entryOdds, closingOdds),
            ClvProbabilityPoints: null,
            ProfitLoss: outcome switch
            {
                BetStatus.Won => stake * (entryOdds - 1.0),
                BetStatus.Lost => -stake,
                BetStatus.Void => 0.0,
                _ => null,
            },
            Payout: null,
            SettledUtc: null,
            Sport: null,
            Competition: null,
            Bookmaker: null,
            EventStartUtc: null);

    [Fact]
    public void An_empty_log_says_there_is_nothing_to_learn_from_yet()
    {
        var summary = BetService.Summarise([]);

        Assert.Equal(0, summary.TotalBets);
        Assert.Null(summary.BeatCloseRate);
        Assert.Contains("No closing prices recorded yet", summary.Readout);
    }

    [Fact]
    public void Rows_without_a_closing_price_are_left_out_of_the_CLV_rate()
    {
        var summary = BetService.Summarise([
            Position(2.10, 2.00),
            Position(2.10, null),
            Position(2.10, null),
        ]);

        Assert.Equal(3, summary.TotalBets);
        Assert.Equal(1, summary.BetsWithClosingPrice);
        Assert.Equal(1.0, summary.BeatCloseRate);
    }

    [Fact]
    public void A_small_sample_is_reported_as_a_small_sample_rather_than_as_evidence()
    {
        var summary = BetService.Summarise(
            Enumerable.Range(0, 10).Select(_ => Position(2.10, 2.00)).ToList());

        Assert.Equal(1.0, summary.BeatCloseRate);
        Assert.Contains("too small a sample", summary.Readout);
    }

    [Fact]
    public void Past_thirty_positions_a_good_rate_is_reported_as_a_working_process()
    {
        var positions = Enumerable.Range(0, 35)
            .Select(i => Position(2.10, i < 21 ? 2.00 : 2.20))   // 21 of 35 = 60%
            .ToList();

        var summary = BetService.Summarise(positions);

        Assert.Equal(21, summary.BetsBeatingClose);
        Assert.Equal(0.6, summary.BeatCloseRate!.Value, 10);
        Assert.Contains("finding real prices", summary.Readout);
    }

    [Fact]
    public void Past_thirty_positions_a_bad_rate_points_at_the_estimates_rather_than_at_luck()
    {
        var positions = Enumerable.Range(0, 40)
            .Select(i => Position(2.10, i < 16 ? 2.00 : 2.20))   // 16 of 40 = 40%
            .ToList();

        var summary = BetService.Summarise(positions);

        Assert.Equal(0.4, summary.BeatCloseRate!.Value, 10);
        Assert.Contains("points at the estimates", summary.Readout);
    }

    [Fact]
    public void Profit_and_loss_only_counts_settled_positions()
    {
        var summary = BetService.Summarise([
            Position(2.50, 2.40, BetStatus.Won, stake: 20.0),   // +30
            Position(2.00, 2.10, BetStatus.Lost, stake: 20.0),  // -20
            Position(2.00, 2.10, BetStatus.Void, stake: 20.0),  //   0
            Position(2.00, null, BetStatus.Open, stake: 20.0),
        ]);

        Assert.Equal(3, summary.SettledBets);
        Assert.Equal(60.0, summary.TotalStaked, 10);
        Assert.Equal(10.0, summary.TotalProfitLoss, 10);
    }
}

public class BetServiceStorageTests
{
    private static BestBetDbContext NewDb()
        => new(new DbContextOptionsBuilder<BestBetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SaveBetRequest SampleSave() => new(
        MatchLabel: "Sunderland - Arsenal",
        MarketKind: MarketKind.NextGoal,
        MarketLabel: "Primul gol: 2",
        Selection: "NextGoalAway",
        EntryDecimalOdds: 1.60,
        EntrySiblingOdds: [3.20, 4.50],
        EntryFairProbability: 0.5389,
        EntryOwnProbability: 0.5459,
        EntryEdgePercentagePoints: 0.70,
        EstimateMethod: "poisson-live-xg",
        BankrollAtEntry: 263.0,
        StakeAmount: 0.0,
        StakeFraction: 0.0,
        Verdict: Verdict.Skip,
        WasLive: true,
        MinutePlayed: 45,
        CumulativeXgHome: 0.13,
        CumulativeXgAway: 0.86,
        GoalsHome: 0,
        GoalsAway: 0,
        Notes: "Model says 54.6% against a price that needs 62.5%.");

    [Fact]
    public async Task A_saved_position_comes_back_with_its_entry_snapshot_intact()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);

        var saved = await service.SaveAsync(SampleSave());

        Assert.True(saved.Id > 0);
        Assert.Equal("Sunderland - Arsenal", saved.MatchLabel);
        Assert.Equal([3.20, 4.50], saved.EntrySiblingOdds);
        Assert.Equal(45, saved.MinutePlayed);
        Assert.Equal(BetStatus.Open, saved.Status);
        Assert.Null(saved.ClvPercent);
    }

    [Fact]
    public async Task Filling_in_the_closing_price_later_is_what_produces_CLV()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.SaveAsync(SampleSave());

        var updated = await service.UpdateAsync(saved.Id, new UpdateBetRequest(
            ClosingDecimalOdds: 1.50,
            ClosingSiblingOdds: [3.60, 5.00],
            Status: BetStatus.Won));

        Assert.NotNull(updated);
        Assert.Equal(1.50, updated!.ClosingDecimalOdds);
        Assert.True(updated.BeatTheClose);                       // 1.60 taken, closed 1.50
        Assert.Equal(1.60 / 1.50 - 1.0, updated.ClvPercent!.Value, 10);
        Assert.NotNull(updated.ClvProbabilityPoints);
        Assert.Equal(BetStatus.Won, updated.Status);
    }

    [Fact]
    public async Task A_partial_update_leaves_the_fields_it_was_not_given_alone()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.SaveAsync(SampleSave());

        await service.UpdateAsync(saved.Id, new UpdateBetRequest(ClosingDecimalOdds: 1.50));
        var afterSecond = await service.UpdateAsync(saved.Id, new UpdateBetRequest(Status: BetStatus.Lost));

        Assert.Equal(1.50, afterSecond!.ClosingDecimalOdds);
        Assert.Equal(BetStatus.Lost, afterSecond.Status);
        Assert.Equal(saved.Notes, afterSecond.Notes);
    }

    [Fact]
    public async Task The_log_reads_newest_first()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);

        var first = await service.SaveAsync(SampleSave() with { MatchLabel = "First" });
        var second = await service.SaveAsync(SampleSave() with { MatchLabel = "Second" });

        var rows = await service.ListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(second.Id, rows[0].Id);
        Assert.Equal(first.Id, rows[1].Id);
    }

    [Fact]
    public async Task Updating_or_deleting_a_row_that_is_not_there_reports_that_rather_than_throwing()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);

        Assert.Null(await service.UpdateAsync(404, new UpdateBetRequest(Status: BetStatus.Won)));
        Assert.False(await service.DeleteAsync(404));
    }

    [Fact]
    public async Task A_deleted_row_is_gone_from_the_log()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.SaveAsync(SampleSave());

        Assert.True(await service.DeleteAsync(saved.Id));
        Assert.Empty(await service.ListAsync());
    }
}
