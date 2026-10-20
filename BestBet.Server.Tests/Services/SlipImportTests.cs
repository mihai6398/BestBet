using BestBet.Contracts;
using BestBet.Data;
using BestBet.Data.Entities;
using BestBet.Domain;
using BestBet.Services;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// A bet placed at a bookmaker, brought in from its slip — "Sub 2" at 2.75, 1-0 in the 62nd minute.
/// </summary>
public class SlipImportTests
{
    private static BestBetDbContext NewDb()
        => new(new DbContextOptionsBuilder<BestBetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ImportBetRequest Slip() => new(
        MatchLabel: "Atletico Madrid - Manchester United",
        MarketKind: MarketKind.TotalGoals,
        MarketLabel: "Sub 2",
        Selection: "Under 2",
        EntryDecimalOdds: 2.75,
        StakeAmount: 2.0);

    [Fact]
    public async Task A_slip_with_no_match_state_is_kept_without_inventing_an_edge()
    {
        using var db = NewDb();
        var saved = await new BetService(db, TestUser.Alice).ImportAsync(Slip());

        Assert.Equal(BetStatus.Open, saved.Status);
        Assert.Equal(2.75, saved.EntryDecimalOdds);
        Assert.Equal(2.0, saved.StakeAmount);
        Assert.Equal("Under 2", saved.Selection);

        // No model ran, so the estimate is the price's own — an edge of exactly zero, not a guess.
        Assert.Equal(1.0 / 2.75, saved.EntryOwnProbability, 10);
        Assert.Equal(0.0, saved.EntryEdgePercentagePoints, 10);
        Assert.Equal("slip-import", saved.EstimateMethod);
        Assert.False(saved.WasLive);
        Assert.Empty(saved.EntrySiblingOdds);
    }

    [Fact]
    public async Task A_slip_with_the_match_state_is_priced_by_the_same_model_as_an_analysis()
    {
        using var db = NewDb();
        var request = Slip() with
        {
            MinutePlayed = 62, GoalsHome = 1, GoalsAway = 0, CumulativeXgHome = 1.1, CumulativeXgAway = 0.5,
        };

        var saved = await new BetService(db, TestUser.Alice).ImportAsync(request);

        var (expected, _) = AnalysisService.PriceLive(
            MarketKind.TotalGoals,
            new MarketSelectionSpec(MarketSelection.Under, 2.0),
            new LiveMatchState(62, 1.1, 0.5, 1, 0, IsPreMatch: false),
            pace: null);

        Assert.Equal(expected.OwnProbability, saved.EntryOwnProbability, 10);
        Assert.Equal("slip-import-model", saved.EstimateMethod);
        Assert.True(saved.WasLive);
        Assert.Equal(62, saved.MinutePlayed);
        Assert.Equal(1, saved.GoalsHome);
    }

    [Fact]
    public async Task An_imported_bet_can_be_re_priced_as_the_match_moves()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.ImportAsync(Slip());

        var quiet = await service.RepriceAsync(saved.Id, new RepriceBetRequest(
            new LiveMatchState(62, 1.1, 0.5, 1, 0, IsPreMatch: false)));
        var afterGoal = await service.RepriceAsync(saved.Id, new RepriceBetRequest(
            new LiveMatchState(70, 1.3, 0.8, 1, 1, IsPreMatch: false)));

        // Under 2 with a 1-0 lead is alive; at 1-1 the next goal makes it a push, so it is worth less.
        Assert.NotNull(quiet);
        Assert.NotNull(afterGoal);
        Assert.True(quiet!.Model.RawWinProbability > afterGoal!.Model.RawWinProbability);
        Assert.True(afterGoal.Model.PushProbability > 0.0);
    }

    [Fact]
    public async Task A_returned_stake_counts_as_nothing_not_as_a_win_in_the_expected_value()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.ImportAsync(Slip());

        var now = await service.RepriceAsync(saved.Id, new RepriceBetRequest(
            new LiveMatchState(62, 1.1, 0.5, 1, 0, IsPreMatch: false)));

        var win = now!.Model.RawWinProbability;
        var push = now.Model.PushProbability;
        Assert.True(push > 0.0);

        // 2.75 x P(win) + P(push) - 1: the stake comes back on a push, so it earns and loses nothing.
        Assert.Equal(win * 2.75 + push - 1.0, now.ExpectedValuePerUnit, 10);
        Assert.Equal(now.ExpectedValuePerUnit * 2.0, now.ExpectedValueAmount, 10);

        // The conditional figure overstates it by 1 / (1 - push), which is what this used to show.
        var conditional = now.Estimate.OwnProbability * 2.75 - 1.0;
        Assert.True(conditional > now.ExpectedValuePerUnit);
    }

    [Fact]
    public async Task A_bet_the_score_has_already_lost_has_no_fair_price_and_still_serialises()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.ImportAsync(Slip());

        // 2-1 against "Under 2": three goals are in, and nothing that happens next can undo that.
        var now = await service.RepriceAsync(saved.Id, new RepriceBetRequest(
            new LiveMatchState(75, 1.5, 0.9, 2, 1, IsPreMatch: false)));

        Assert.Equal(0.0, now!.Model.RawWinProbability, 10);
        Assert.Null(now.FairOddsNow);
        Assert.Equal(-1.0, now.ExpectedValuePerUnit, 10);

        // Infinity is not JSON: this is what used to turn the response into a 500.
        var json = System.Text.Json.JsonSerializer.Serialize(now);
        Assert.Contains("\"FairOddsNow\":null", json);
    }

    [Fact]
    public async Task On_a_half_line_nothing_can_push_so_the_expected_value_is_unchanged()
    {
        using var db = NewDb();
        var service = new BetService(db, TestUser.Alice);
        var saved = await service.ImportAsync(Slip() with { MarketLabel = "Sub 2.5", Selection = "Under 2.5" });

        var now = await service.RepriceAsync(saved.Id, new RepriceBetRequest(
            new LiveMatchState(62, 1.1, 0.5, 1, 0, IsPreMatch: false)));

        Assert.Equal(0.0, now!.Model.PushProbability);
        Assert.Equal(now.Estimate.OwnProbability * 2.75 - 1.0, now.ExpectedValuePerUnit, 10);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    public async Task A_price_at_or_below_evens_is_refused(double price)
    {
        using var db = NewDb();

        await Assert.ThrowsAsync<AnalysisInputException>(
            () => new BetService(db, TestUser.Alice).ImportAsync(Slip() with { EntryDecimalOdds = price }));
    }

    [Fact]
    public async Task A_side_that_cannot_be_read_back_is_refused_rather_than_saved_untrackable()
    {
        using var db = NewDb();

        await Assert.ThrowsAsync<AnalysisInputException>(
            () => new BetService(db, TestUser.Alice).ImportAsync(Slip() with { Selection = "somewhere" }));
    }

    [Fact]
    public async Task A_negative_stake_is_refused()
    {
        using var db = NewDb();

        await Assert.ThrowsAsync<AnalysisInputException>(
            () => new BetService(db, TestUser.Alice).ImportAsync(Slip() with { StakeAmount = -1.0 }));
    }
}
