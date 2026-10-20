using System.Net;
using System.Net.Http.Json;
using BestBet.Contracts;
using BestBet.Data.Entities;
using BestBet.Domain;

namespace BestBet.Server.Tests.Integration;

/// <summary>
/// One user can neither see nor touch another's bets or money — and cannot even tell whether a bet id
/// belongs to someone else, because the answer is the same 404 as for an id nobody has.
/// </summary>
[Collection(ApiCollection.Name)]
public class IsolationTests(BestBetFactory app)
{
    internal static SaveBetRequest ABet(string match = "Sunderland - Arsenal", double stake = 10.0) => new(
        MatchLabel: match,
        MarketKind: MarketKind.TotalGoals,
        MarketLabel: "Peste 2.5",
        Selection: "Over 2.5",
        EntryDecimalOdds: 2.30,
        EntrySiblingOdds: [1.60],
        EntryFairProbability: 0.41,
        EntryOwnProbability: 0.52,
        EntryEdgePercentagePoints: 11.0,
        EstimateMethod: "poisson-live-xg-paced",
        BankrollAtEntry: 100.0,
        StakeAmount: stake,
        StakeFraction: stake / 100.0,
        Verdict: Verdict.Bet,
        WasLive: false,
        MinutePlayed: null,
        CumulativeXgHome: null,
        CumulativeXgAway: null,
        GoalsHome: null,
        GoalsAway: null,
        Notes: null);

    private async Task<(HttpClient Alice, HttpClient Bob, BetDto AlicesBet)> TwoUsersAndABetAsync()
    {
        var alice = await app.SignedInAsync();
        var bob = await app.SignedInAsync();

        var created = await alice.PostAsJsonAsync("/api/bets", ABet());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var bet = (await created.Content.ReadFromJsonAsync<BetDto>(AuthTests.Json))!;

        return (alice, bob, bet);
    }

    [Fact]
    public async Task A_user_cannot_read_another_users_bet()
    {
        var (_, bob, bet) = await TwoUsersAndABetAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/bets/{bet.Id}")).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<List<BetDto>>("/api/bets", AuthTests.Json))!);
    }

    [Fact]
    public async Task Someone_elses_bet_looks_exactly_like_one_that_does_not_exist()
    {
        var (_, bob, bet) = await TwoUsersAndABetAsync();

        var theirs = await bob.GetAsync($"/api/bets/{bet.Id}");
        var nobodys = await bob.GetAsync("/api/bets/999999999");

        Assert.Equal(nobodys.StatusCode, theirs.StatusCode);
        Assert.Equal(await WithoutTraceId(nobodys), await WithoutTraceId(theirs));
    }

    /// <summary>The body minus the per-request trace id, which differs on every response by design.</summary>
    private static async Task<string> WithoutTraceId(HttpResponseMessage response)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    [Fact]
    public async Task A_user_cannot_change_another_users_bet()
    {
        var (alice, bob, bet) = await TwoUsersAndABetAsync();

        var put = await bob.PutAsJsonAsync($"/api/bets/{bet.Id}", new UpdateBetRequest(StakeAmount: 999.0, Notes: "mine now"));
        var patch = await bob.PatchAsJsonAsync($"/api/bets/{bet.Id}", new UpdateBetRequest(EntryDecimalOdds: 9.0));
        var settle = await bob.PostAsJsonAsync($"/api/bets/{bet.Id}/settle", new SettleBetRequest(BetStatus.Lost), AuthTests.Json);

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settle.StatusCode);

        var unchanged = (await alice.GetFromJsonAsync<BetDto>($"/api/bets/{bet.Id}", AuthTests.Json))!;
        Assert.Equal(10.0, unchanged.StakeAmount);
        Assert.Equal(2.30, unchanged.EntryDecimalOdds);
        Assert.Equal(BetStatus.Open, unchanged.Status);
        Assert.Null(unchanged.Notes);
    }

    [Fact]
    public async Task A_user_cannot_delete_another_users_bet()
    {
        var (alice, bob, bet) = await TwoUsersAndABetAsync();

        var delete = await bob.DeleteAsync($"/api/bets/{bet.Id}");

        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/bets/{bet.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_reprice_another_users_bet()
    {
        var (_, bob, bet) = await TwoUsersAndABetAsync();

        var reprice = await bob.PostAsJsonAsync(
            $"/api/bets/{bet.Id}/reprice",
            new RepriceBetRequest(new LiveMatchState(60, 1.2, 0.8, 1, 0, false)));

        Assert.Equal(HttpStatusCode.NotFound, reprice.StatusCode);
    }

    [Fact]
    public async Task An_owner_sent_in_the_body_is_ignored()
    {
        // The owner always comes from the session. A body that names someone else still saves the bet
        // as the sender's own, and the person it names never sees it.
        var alice = await app.SignedInAsync();
        var bob = await app.SignedInAsync();

        var body = JsonContent.Create(new Dictionary<string, object?>
        {
            ["userId"] = TestUser.Alice.Id,
            ["matchLabel"] = "Smuggled - In",
            ["marketKind"] = "TotalGoals",
            ["marketLabel"] = "Peste 2.5",
            ["selection"] = "Over 2.5",
            ["entryDecimalOdds"] = 2.0,
            ["entrySiblingOdds"] = new[] { 1.8 },
            ["entryFairProbability"] = 0.47,
            ["entryOwnProbability"] = 0.5,
            ["entryEdgePercentagePoints"] = 3.0,
            ["estimateMethod"] = "manual-input",
            ["bankrollAtEntry"] = 100.0,
            ["stakeAmount"] = 5.0,
            ["stakeFraction"] = 0.05,
            ["verdict"] = "Bet",
            ["wasLive"] = false,
        });

        var created = await bob.PostAsync("/api/bets", body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Single((await bob.GetFromJsonAsync<List<BetDto>>("/api/bets", AuthTests.Json))!);
        Assert.Empty((await alice.GetFromJsonAsync<List<BetDto>>("/api/bets", AuthTests.Json))!);
    }

    [Fact]
    public async Task Statistics_count_only_the_users_own_bets()
    {
        var (_, bob, _) = await TwoUsersAndABetAsync();

        var stats = (await bob.GetFromJsonAsync<BetStats>("/api/bets/stats", AuthTests.Json))!;

        Assert.Equal(0, stats.TotalBets);
        Assert.Equal(0, stats.OpenBets);
    }

    [Fact]
    public async Task A_user_cannot_see_or_remove_another_users_money()
    {
        var alice = await app.SignedInAsync();
        var bob = await app.SignedInAsync();

        var deposit = await alice.PostAsJsonAsync(
            "/api/bankroll/transactions",
            new AddBankrollTransactionRequest(BankrollTransactionKind.Deposit, 500.0, "starting balance"),
            AuthTests.Json);
        var aliceBankroll = (await deposit.Content.ReadFromJsonAsync<BankrollSummary>(AuthTests.Json))!;
        var transactionId = aliceBankroll.Transactions.Single().Id;

        var bobBankroll = (await bob.GetFromJsonAsync<BankrollSummary>("/api/bankroll", AuthTests.Json))!;
        var bobDelete = await bob.DeleteAsync($"/api/bankroll/transactions/{transactionId}");

        Assert.Equal(0.0, bobBankroll.Balance);
        Assert.Empty(bobBankroll.Transactions);
        Assert.Equal(HttpStatusCode.NotFound, bobDelete.StatusCode);
        Assert.Equal(500.0, (await alice.GetFromJsonAsync<BankrollSummary>("/api/bankroll", AuthTests.Json))!.Balance);
    }

    [Fact]
    public async Task One_users_bookmarklet_token_does_not_feed_another_user()
    {
        var alice = await app.SignedInAsync();
        var bob = await app.SignedInAsync();

        var aliceToken = (await alice.GetFromJsonAsync<LiveFeedState>("/api/live-feed", AuthTests.Json))!.Token;
        var bobToken = (await bob.GetFromJsonAsync<LiveFeedState>("/api/live-feed", AuthTests.Json))!.Token;

        var push = await app.Browser(withAppHeader: false).PostAsync(
            "/api/live-feed",
            new StringContent($$"""{"token":"{{aliceToken}}","text":"minute 55"}"""));

        Assert.NotEqual(aliceToken, bobToken);
        Assert.Equal(HttpStatusCode.NoContent, push.StatusCode);
        Assert.Equal("minute 55", (await alice.GetFromJsonAsync<LiveFeedState>("/api/live-feed", AuthTests.Json))!.Text);
        Assert.Null((await bob.GetFromJsonAsync<LiveFeedState>("/api/live-feed", AuthTests.Json))!.Text);
    }
}
