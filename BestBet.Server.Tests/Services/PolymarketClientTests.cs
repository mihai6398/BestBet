using System.Net;
using System.Text;
using BestBet.Services;
using Microsoft.Extensions.Caching.Memory;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// Polymarket's search only returns a match's headline event (the 1X2 markets). The totals live in a
/// sibling "-more-markets" event that has to be fetched by slug — these pin that down without a network.
/// </summary>
public class PolymarketClientTests
{
    private const string Search = """
        {"events":[{"title":"Home vs. Away","slug":"ucl-hom-awa-2026-10-13","markets":[
          {"id":"1","question":"Will Home win?","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.55\",\"0.45\"]",
           "active":true,"closed":false,"archived":false,"volumeNum":50000,"sportsMarketType":"moneyline"}]}]}
        """;

    private const string MoreMarkets = """
        [{"title":"Home vs. Away - More Markets","slug":"ucl-hom-awa-2026-10-13-more-markets","markets":[
          {"id":"2","question":"Home vs. Away: O/U 2.5","outcomes":"[\"Over\",\"Under\"]","outcomePrices":"[\"0.575\",\"0.425\"]",
           "active":true,"closed":false,"archived":false,"volumeNum":27000,"sportsMarketType":"totals","line":2.5},
          {"id":"3","question":"Home vs. Away: O/U 1.5","outcomes":"[\"Over\",\"Under\"]","outcomePrices":"[\"0.8\",\"0.2\"]",
           "active":true,"closed":false,"archived":false,"volumeNum":6000,"sportsMarketType":"totals","line":1.5},
          {"id":"4","question":"Exact Score: Home 1 - 0 Away?","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.1\",\"0.9\"]",
           "active":true,"closed":false,"archived":false,"volumeNum":30000,"sportsMarketType":"soccer_exact_score"}]}]
        """;

    private sealed class FakeHandler(Func<Uri, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (status, body) = reply(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static PolymarketClient Client(Func<Uri, (HttpStatusCode, string)> reply) => new(
        new HttpClient(new FakeHandler(reply)) { BaseAddress = new Uri("https://gamma.test/") },
        new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Totals_from_the_more_markets_event_are_offered_next_to_the_1X2()
    {
        var client = Client(uri => uri.AbsolutePath.Contains("public-search")
            ? (HttpStatusCode.OK, Search)
            : (HttpStatusCode.OK, MoreMarkets));

        var markets = await client.SearchAsync("home away");

        Assert.Equal(["moneyline", "totals", "totals"], markets.Select(m => m.MarketType));
        // Ascending by line, so Over 1.5 is next to Over 2.5 rather than ordered by who traded more.
        Assert.Equal([1.5, 2.5], markets.Where(m => m.MarketType == "totals").Select(m => m.Line));
        // Both halves read as the same match.
        Assert.Single(markets.Select(m => m.EventTitle).Distinct());

        var over25 = markets.Single(m => m.Line == 2.5);
        Assert.Equal("Over", over25.Outcomes[0].Label);
        Assert.Equal(0.575, over25.Outcomes[0].Probability, 10);
        Assert.Equal(1.0 / 0.575, over25.Outcomes[0].DecimalOdds, 10);
    }

    [Fact]
    public async Task Kinds_with_no_form_in_the_app_are_left_out()
        => Assert.DoesNotContain(
            await Client(uri => uri.AbsolutePath.Contains("public-search")
                ? (HttpStatusCode.OK, Search)
                : (HttpStatusCode.OK, MoreMarkets)).SearchAsync("home away"),
            m => m.MarketType == "soccer_exact_score");

    [Fact]
    public async Task A_failing_sibling_lookup_still_leaves_the_1X2_markets()
    {
        var client = Client(uri => uri.AbsolutePath.Contains("public-search")
            ? (HttpStatusCode.OK, Search)
            : (HttpStatusCode.InternalServerError, "boom"));

        var markets = await client.SearchAsync("home away");

        Assert.Single(markets);
        Assert.Equal("moneyline", markets[0].MarketType);
    }
}
