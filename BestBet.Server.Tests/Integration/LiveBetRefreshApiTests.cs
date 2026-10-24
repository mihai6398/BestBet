using System.Net;
using System.Net.Http.Json;
using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Integration;

/// <summary>
/// Every open bet followed through its own match: the server finds it at FotMob by the bet's team
/// names and re-prices it, which is what the browser asks for once a minute while monitoring is on.
/// FotMob is the fixture stand-in from <see cref="FixtureFotMob"/>.
/// </summary>
[Collection(ApiCollection.Name)]
public class LiveBetRefreshApiTests(BestBetFactory app)
{
    private async Task<(HttpClient Client, int Id)> ABetOnAsync(string match)
    {
        var client = await app.SignedInAsync();
        var created = await client.PostAsJsonAsync("/api/bets", IsolationTests.ABet(match));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (client, (await created.Content.ReadFromJsonAsync<BetDto>(AuthTests.Json))!.Id);
    }

    private static async Task<LiveRepriceResult> RefreshAsync(HttpClient client, int id)
    {
        var response = await client.PostAsJsonAsync($"/api/bets/{id}/reprice/live", new LiveRepriceRequest(), AuthTests.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LiveRepriceResult>(AuthTests.Json))!;
    }

    [Fact]
    public async Task A_bet_on_a_match_in_progress_is_priced_off_that_match()
    {
        var (client, id) = await ABetOnAsync("Como - Manchester United");

        var result = await RefreshAsync(client, id);

        // The match it found is in the answer, so the user can check it is the one they bet on.
        Assert.Equal("Como - Manchester United", result.Reading!.MatchLabel);
        Assert.Null(result.Unavailable);
        Assert.NotNull(result.Repriced);

        // First-half stoppage on a +1 board, carried across from the reading into the model.
        Assert.Equal(46, result.LiveState!.MinutePlayed);
        Assert.Equal(MatchPeriod.FirstHalf, result.LiveState.Period);
        Assert.Equal(1, result.LiveState.AddedTimeFirstHalf);
        Assert.Equal(MatchClock.MinimumMinutesLeft + 45 + MatchClock.ExpectedSecondHalfStoppage,
            result.Repriced!.Model.RemainingMinutes, 10);

        // And the recent window came from the reading's shot map, not from anything the caller sent.
        Assert.Contains("Last 20 minutes", result.Repriced.Estimate.Notes);
    }

    [Fact]
    public async Task A_short_or_differently_spelled_label_still_finds_the_match()
    {
        // A slip or a hand-typed label rarely spells a club the way FotMob does.
        var (client, id) = await ABetOnAsync("Como v Manchester Utd");

        Assert.Equal("Como - Manchester United", (await RefreshAsync(client, id)).Reading!.MatchLabel);
    }

    [Theory]
    [InlineData("Everton - Chelsea", ProviderLiveState.NotStarted)]
    [InlineData("VfB Stuttgart - Atlético Madrid", ProviderLiveState.Finished)]
    [InlineData("Club Bolivar - Olimpia", ProviderLiveState.NoXg)]
    [InlineData("Nobody United - Nowhere Rovers", ProviderLiveState.NotFound)]
    public async Task A_match_that_cannot_be_priced_says_why_rather_than_failing(string match, string reason)
    {
        var (client, id) = await ABetOnAsync(match);

        var result = await RefreshAsync(client, id);

        Assert.Equal(reason, result.Unavailable);
        Assert.Null(result.Repriced);
    }

    [Fact]
    public async Task Another_users_bet_is_not_found()
    {
        var (_, id) = await ABetOnAsync("Como - Manchester United");
        var bob = await app.SignedInAsync();

        var response = await bob.PostAsJsonAsync($"/api/bets/{id}/reprice/live", new LiveRepriceRequest(), AuthTests.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Corrections_off_means_off_even_with_a_shot_map_in_the_reading()
    {
        var (client, id) = await ABetOnAsync("Como - Manchester United");

        var response = await client.PostAsJsonAsync(
            $"/api/bets/{id}/reprice/live", new LiveRepriceRequest(PaceSettings.Flat), AuthTests.Json);
        var result = (await response.Content.ReadFromJsonAsync<LiveRepriceResult>(AuthTests.Json))!;

        Assert.False(result.Repriced!.Model.Pace.IsAdjusted);
        Assert.Equal(PoissonGoalModel.RemainingMinutes(46), result.Repriced.Model.RemainingMinutes);
    }
}
