using System.Net;
using System.Net.Http.Json;
using System.Text;
using BestBet.Contracts;

namespace BestBet.Server.Tests.Integration;

/// <summary>
/// The match context — half, boards, red cards, recent xG — going over the wire exactly as the
/// client sends it, and coming back as the time the model ran the rest of the match over.
/// </summary>
[Collection(ApiCollection.Name)]
public class LiveRepriceApiTests(BestBetFactory app)
{
    private static async Task<(double Remaining, string Notes)> RepriceAsync(HttpClient client, int id, string body)
    {
        var response = await client.PostAsync(
            $"/api/bets/{id}/reprice", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var repriced = (await response.Content.ReadFromJsonAsync<RepricedBet>(AuthTests.Json))!;
        return (repriced.Model.RemainingMinutes, repriced.Estimate.Notes ?? string.Empty);
    }

    private async Task<(HttpClient Client, int Id)> ABetAsync()
    {
        var client = await app.SignedInAsync();
        var created = await client.PostAsJsonAsync("/api/bets", IsolationTests.ABet());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (client, (await created.Content.ReadFromJsonAsync<BetDto>(AuthTests.Json))!.Id);
    }

    [Fact]
    public async Task The_period_and_the_board_arrive_as_the_client_writes_them()
    {
        var (client, id) = await ABetAsync();

        // "45+2" on a +3 board: a minute and a half of the first half, then the whole second one.
        var (remaining, _) = await RepriceAsync(client, id, """
            {
              "liveState": {
                "minutePlayed": 47, "cumulativeXgHome": 0.9, "cumulativeXgAway": 0.4,
                "currentGoalsHome": 1, "currentGoalsAway": 0, "isPreMatch": false,
                "period": "FirstHalf", "addedTimeFirstHalf": 3, "addedTimeSecondHalf": null,
                "redCardsHome": 0, "redCardsAway": 1, "lastRedCardMinute": 40
              },
              "pace": {
                "shrink": true, "scoreState": true, "stoppage": true, "redCards": true,
                "recentWindowMinutes": 20, "recentXgHome": 0.35, "recentXgAway": 0.05
              }
            }
            """);

        Assert.Equal(51, remaining, 10);
    }

    [Fact]
    public async Task A_client_that_sends_none_of_it_gets_the_usual_stoppage()
    {
        // The older request shape still works, and now counts the stoppage it used to leave out.
        var (client, id) = await ABetAsync();

        var (remaining, notes) = await RepriceAsync(client, id, """
            {
              "liveState": {
                "minutePlayed": 80, "cumulativeXgHome": 1.1, "cumulativeXgAway": 0.9,
                "currentGoalsHome": 1, "currentGoalsAway": 1, "isPreMatch": false
              }
            }
            """);

        Assert.Equal(14.5, remaining, 10);
        Assert.Contains("Stoppage counted", notes);
    }

    [Fact]
    public async Task Switching_the_corrections_off_brings_the_specs_clock_back()
    {
        var (client, id) = await ABetAsync();

        var (remaining, _) = await RepriceAsync(client, id, """
            {
              "liveState": {
                "minutePlayed": 80, "cumulativeXgHome": 1.1, "cumulativeXgAway": 0.9,
                "currentGoalsHome": 1, "currentGoalsAway": 1, "isPreMatch": false,
                "addedTimeSecondHalf": 6
              },
              "pace": { "shrink": false, "scoreState": false, "stoppage": false, "redCards": false }
            }
            """);

        Assert.Equal(10, remaining, 10);
    }
}
