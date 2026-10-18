using System.Net;
using System.Net.Http.Json;
using BestBet.Contracts;
using BestBet.Data.Entities;

namespace BestBet.Server.Tests.Integration;

/// <summary>Saving, settling and counting bets, and the balance that follows, through the real API.</summary>
[Collection(ApiCollection.Name)]
public class BetsApiTests(BestBetFactory app)
{
    private static async Task<BetDto> SaveAsync(HttpClient client, string match = "A - B", double stake = 10.0)
    {
        var response = await client.PostAsJsonAsync("/api/bets", IsolationTests.ABet(match, stake));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BetDto>(AuthTests.Json))!;
    }

    private static async Task<BetDto> SettleAsync(HttpClient client, int id, SettleBetRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/bets/{id}/settle", request, AuthTests.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BetDto>(AuthTests.Json))!;
    }

    [Fact]
    public async Task Settling_works_out_the_payout_and_the_profit()
    {
        var client = await app.SignedInAsync();

        var won = await SettleAsync(client, (await SaveAsync(client)).Id, new SettleBetRequest(BetStatus.Won));
        var halfWon = await SettleAsync(client, (await SaveAsync(client)).Id, new SettleBetRequest(BetStatus.HalfWon));
        var cashedOut = await SettleAsync(client, (await SaveAsync(client)).Id, new SettleBetRequest(BetStatus.CashedOut, Payout: 14.5));

        // 10 at 2.30: a win pays 23.00; half won pays 5 at 2.30 plus 5 back, 16.50; a cash-out pays what it paid.
        Assert.Equal((23.0, 13.0), (won.Payout!.Value, won.ProfitLoss!.Value));
        Assert.Equal((16.5, 6.5), (halfWon.Payout!.Value, halfWon.ProfitLoss!.Value));
        Assert.Equal((14.5, 4.5), (cashedOut.Payout!.Value, cashedOut.ProfitLoss!.Value));
        Assert.NotNull(won.SettledUtc);
    }

    [Fact]
    public async Task A_cash_out_without_the_amount_is_refused()
    {
        var client = await app.SignedInAsync();
        var bet = await SaveAsync(client);

        var response = await client.PostAsJsonAsync($"/api/bets/{bet.Id}/settle", new SettleBetRequest(BetStatus.CashedOut), AuthTests.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_balance_follows_deposits_withdrawals_and_settled_bets()
    {
        var client = await app.SignedInAsync();

        await client.PostAsJsonAsync("/api/bankroll/transactions", new AddBankrollTransactionRequest(BankrollTransactionKind.Deposit, 500.0), AuthTests.Json);
        await client.PostAsJsonAsync("/api/bankroll/transactions", new AddBankrollTransactionRequest(BankrollTransactionKind.Deposit, 200.0), AuthTests.Json);
        await client.PostAsJsonAsync("/api/bankroll/transactions", new AddBankrollTransactionRequest(BankrollTransactionKind.Withdrawal, 50.0), AuthTests.Json);

        var winner = await SaveAsync(client, stake: 10.0);
        await SettleAsync(client, winner.Id, new SettleBetRequest(BetStatus.Won));   // +13.00
        await SaveAsync(client, stake: 20.0);                                       // open

        var bankroll = (await client.GetFromJsonAsync<BankrollSummary>("/api/bankroll", AuthTests.Json))!;

        Assert.Equal(663.0, bankroll.Balance, 2);       // 500 + 200 - 50 + 13
        Assert.Equal(13.0, bankroll.SettledProfitLoss, 2);
        Assert.Equal(20.0, bankroll.OpenStake, 2);
        Assert.Equal(643.0, bankroll.Available, 2);
        Assert.True(bankroll.HasStartingBalance);
    }

    [Fact]
    public async Task Withdrawing_more_than_is_available_is_refused()
    {
        var client = await app.SignedInAsync();
        await client.PostAsJsonAsync("/api/bankroll/transactions", new AddBankrollTransactionRequest(BankrollTransactionKind.Deposit, 100.0), AuthTests.Json);
        await SaveAsync(client, stake: 60.0);

        var response = await client.PostAsJsonAsync(
            "/api/bankroll/transactions",
            new AddBankrollTransactionRequest(BankrollTransactionKind.Withdrawal, 50.0),
            AuthTests.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stats_report_yield_roi_and_what_is_still_open()
    {
        var client = await app.SignedInAsync();
        await client.PostAsJsonAsync("/api/bankroll/transactions", new AddBankrollTransactionRequest(BankrollTransactionKind.Deposit, 100.0), AuthTests.Json);

        await SettleAsync(client, (await SaveAsync(client, stake: 10.0)).Id, new SettleBetRequest(BetStatus.Won));  // +13
        await SettleAsync(client, (await SaveAsync(client, stake: 10.0)).Id, new SettleBetRequest(BetStatus.Lost)); // -10
        await SaveAsync(client, stake: 5.0);

        var stats = (await client.GetFromJsonAsync<BetStats>("/api/bets/stats", AuthTests.Json))!;

        Assert.Equal(3, stats.TotalBets);
        Assert.Equal(1, stats.OpenBets);
        Assert.Equal(5.0, stats.OpenStake, 2);
        Assert.Equal(20.0, stats.TotalStaked, 2);
        Assert.Equal(3.0, stats.TotalProfitLoss, 2);
        Assert.Equal(0.15, stats.Yield!.Value, 4);   // 3 / 20
        Assert.Equal(0.03, stats.Roi!.Value, 4);     // 3 / 100
    }

    [Fact]
    public async Task The_list_filters_by_status_and_pages_with_a_total()
    {
        var client = await app.SignedInAsync();
        for (var i = 0; i < 3; i++)
        {
            await SaveAsync(client, $"Open {i}");
        }

        await SettleAsync(client, (await SaveAsync(client, "Settled")).Id, new SettleBetRequest(BetStatus.Void));

        var open = (await client.GetFromJsonAsync<List<BetDto>>("/api/bets?status=Open", AuthTests.Json))!;
        var closed = (await client.GetFromJsonAsync<List<BetDto>>("/api/bets?status=closed", AuthTests.Json))!;
        var page = await client.GetAsync("/api/bets?page=2&pageSize=3");
        var secondPage = (await page.Content.ReadFromJsonAsync<List<BetDto>>(AuthTests.Json))!;

        Assert.Equal(3, open.Count);
        Assert.All(open, b => Assert.Equal(BetStatus.Open, b.Status));
        Assert.Equal("Settled", Assert.Single(closed).MatchLabel);
        Assert.Single(secondPage);
        Assert.Equal("4", page.Headers.GetValues("X-Total-Count").Single());
    }

    [Theory]
    [InlineData(1.0, 10.0)]
    [InlineData(0.5, 10.0)]
    [InlineData(2.0, -1.0)]
    public async Task Nonsense_prices_and_stakes_are_refused(double odds, double stake)
    {
        var client = await app.SignedInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/bets",
            IsolationTests.ABet() with { EntryDecimalOdds = odds, StakeAmount = stake });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Money_is_kept_to_the_cent()
    {
        var client = await app.SignedInAsync();

        var bet = await SaveAsync(client, stake: 3.885);

        Assert.Equal(3.89, bet.StakeAmount);
    }
}
