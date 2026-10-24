using BestBet.Contracts;
using BestBet.Services.Scanner;

namespace BestBet.Server.Tests.Scanner;

/// <summary>
/// Price → odds, on the captured Liverpool–Brighton books where the numbers can be checked by hand.
/// </summary>
public class PolymarketOddsTests
{
    [Theory]
    [InlineData(0.46, 2.1739130434782608)]
    [InlineData(0.5, 2.0)]
    [InlineData(0.25, 4.0)]
    [InlineData(0.64, 1.5625)]
    public void Decimal_odds_are_the_reciprocal_of_the_price(double price, double odds)
        => Assert.Equal(odds, PolymarketOdds.DecimalOdds(price)!.Value, 12);

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    [InlineData(1.2)]
    [InlineData(null)]
    public void Anything_that_is_not_a_price_has_no_odds(double? price)
        => Assert.Null(PolymarketOdds.DecimalOdds(price));

    [Fact]
    public void The_book_lists_its_best_ask_last_and_the_back_odds_come_from_it()
    {
        var book = PolymarketFixtures.Read<ClobBook>("book-liv-bri-over-2.5");

        // Asks descending, bids ascending: the first of each is the worst price, not the best.
        Assert.Equal(0.99, book.Asks![0].Price);
        Assert.Equal(0.01, book.Bids![0].Price);

        var top = TopOfBook.FromBook(book);
        Assert.Equal(0.64, top.Ask);
        Assert.Equal(1683.2, top.AskSize);
        Assert.Equal(0.63, top.Bid);
        Assert.Equal(0.635, top.Mid!.Value, 12);
        Assert.Equal(0.01, top.Spread!.Value, 12);
        Assert.Equal(1.5625, PolymarketOdds.DecimalOdds(top.Ask)!.Value, 12);
    }

    /// <summary>
    /// Under is priced from Under's own book. It mirrors Over's — Under's ask is 1 − Over's bid, size
    /// for size — which is not the same thing as 1 − Over's mid: that ignores the spread.
    /// </summary>
    [Fact]
    public void Under_backs_at_its_own_ask_not_at_one_minus_the_over_price()
    {
        var over = TopOfBook.FromBook(PolymarketFixtures.Read<ClobBook>("book-liv-bri-over-2.5"));
        var under = TopOfBook.FromBook(PolymarketFixtures.Read<ClobBook>("book-liv-bri-under-2.5"));

        Assert.Equal(0.37, under.Ask);
        Assert.Equal(1.0 - over.Bid!.Value, under.Ask!.Value, 12);
        Assert.Equal(over.BidSize, under.AskSize);

        Assert.Equal(2.7027, PolymarketOdds.DecimalOdds(under.Ask)!.Value, 4);
        Assert.Equal(2.7397, PolymarketOdds.DecimalOdds(1.0 - over.Mid!.Value)!.Value, 4);
    }

    [Fact]
    public void Without_a_book_the_second_token_is_read_as_the_mirror_of_gammas_quote()
    {
        var total = PolymarketFixtures.LiverpoolBrightonMoreMarkets().Markets!
            .Single(m => m.SportsMarketType == "totals" && m.Line == 2.5);

        var over = TopOfBook.FromGamma(total, 0);
        var under = TopOfBook.FromGamma(total, 1);

        Assert.Equal((0.63, 0.64), (over.Bid, over.Ask));
        Assert.Equal(0.36, under.Bid!.Value, 12);
        Assert.Equal(0.37, under.Ask!.Value, 12);
        Assert.Equal(PriceSource.Gamma, under.Source);
        Assert.Null(under.AskSize);
    }

    [Fact]
    public void An_empty_side_of_the_book_is_no_price_rather_than_zero_or_one()
    {
        var book = new ClobBook { Bids = [], Asks = [new ClobLevel { Price = 1.0, Size = 10 }] };

        var top = TopOfBook.FromBook(book);

        Assert.Null(top.Bid);
        Assert.Null(top.Ask);
        Assert.Null(top.Mid);
    }

    /// <summary>The 1X2 is three Yes tokens; their asks add up to 1.02 — a 2% book.</summary>
    [Fact]
    public void The_1X2_overround_is_the_sum_of_the_three_yes_asks()
    {
        var books = PolymarketFixtures.Books("books-liv-bri");
        var asks = PolymarketFixtures.LiverpoolBrighton().Markets!
            .Select(m => TopOfBook.FromBook(books.Single(b => b.AssetId == m.TokenIds()[0])).Ask)
            .ToList();

        Assert.Equal([0.46, 0.25, 0.31], asks);
        Assert.Equal(1.02, PolymarketOdds.Overround(asks)!.Value, 12);

        var fair = PolymarketOdds.FairProbabilities(asks);
        Assert.Equal(1.0, fair.Sum(p => p!.Value), 12);
        Assert.Equal(0.46 / 1.02, fair[0]!.Value, 12);
    }

    [Fact]
    public void A_market_with_a_missing_ask_has_no_overround_and_no_fair_line()
    {
        double?[] asks = [0.46, null, 0.31];

        Assert.Null(PolymarketOdds.Overround(asks));
        Assert.All(PolymarketOdds.FairProbabilities(asks), Assert.Null);
    }

    /// <summary>Polymarket's own worked example: 100 shares at 50¢ on a sports market pay $1.25.</summary>
    [Fact]
    public void The_taker_fee_matches_polymarkets_worked_example()
    {
        var fee = new FeeModel(0.05, 1.0);

        Assert.Equal(1.25, 100 * fee.PerShare(0.50), 12);
        Assert.Equal(0.0, fee.PerShare(0.0));
        Assert.Equal(0.0, FeeModel.None.PerShare(0.5));
    }

    [Fact]
    public void The_net_odds_add_the_fee_to_what_each_share_costs()
    {
        var yes = PolymarketFixtures.LiverpoolBrighton().Markets!.First();
        var fee = yes.Fee();

        Assert.Equal(new FeeModel(0.05, 1.0), fee);
        // 0.46 + 0.05 × 0.46 × 0.54 = 0.47242 a share.
        Assert.Equal(1.0 / 0.47242, PolymarketOdds.NetDecimalOdds(0.46, fee)!.Value, 10);
        Assert.Equal(2.1168, PolymarketOdds.NetDecimalOdds(0.46, fee)!.Value, 4);
        Assert.Equal(PolymarketOdds.DecimalOdds(0.46), PolymarketOdds.NetDecimalOdds(0.46, FeeModel.None));
    }

    [Fact]
    public void A_market_without_fees_enabled_charges_nothing()
        => Assert.Equal(FeeModel.None, new GammaMarket { FeesEnabled = false, FeeSchedule = new() { Rate = 0.05 } }.Fee());

    [Fact]
    public void Liquidity_at_the_price_is_the_resting_shares_times_their_cost()
        => Assert.Equal(0.64 * 1683.2, PolymarketOdds.LiquidityAtPrice(0.64, 1683.2)!.Value, 10);

    [Fact]
    public void The_embedded_arrays_and_gammas_own_date_format_are_read()
    {
        var market = PolymarketFixtures.LiverpoolBrighton().Markets!.First();

        Assert.Equal(["Yes", "No"], market.OutcomeLabels());
        Assert.Equal(2, market.TokenIds().Length);
        Assert.True(market.IsBinary);
        // "2026-10-25 14:00:00+00" is not ISO; it still has to come out as the kick-off.
        Assert.Equal(new DateTime(2026, 10, 25, 14, 0, 0, DateTimeKind.Utc), market.GameStartUtc);
        Assert.Equal(new DateTime(2026, 10, 25, 14, 0, 0, DateTimeKind.Utc), PolymarketFixtures.LiverpoolBrighton().StartUtc);
        Assert.Null(new GammaMarket { GameStartTime = "soon" }.GameStartUtc);
    }

    [Fact]
    public void A_steady_slide_is_one_big_move_and_noise_is_none()
    {
        var start = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        double[] prices = [0.50, 0.50, 0.51, 0.49, 0.46, 0.43, 0.42, 0.42, 0.43, 0.44];
        var points = prices.Select((p, i) => new PricePoint(start.AddHours(i), p)).ToList();

        var moves = PriceMoves.Detect(points, TimeSpan.FromHours(3), 0.05);

        var move = Assert.Single(moves);
        Assert.Equal(0.51, move.FromPrice);
        Assert.Equal(0.42, move.ToPrice);
        Assert.Equal(start.AddHours(2), move.From);
        Assert.Equal(start.AddHours(6), move.To);

        Assert.Empty(PriceMoves.Detect(points, TimeSpan.FromHours(3), 0.20));
    }

    [Fact]
    public void A_move_back_the_other_way_is_a_second_marker()
    {
        var start = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        double[] prices = [0.50, 0.40, 0.40, 0.40, 0.52];
        var points = prices.Select((p, i) => new PricePoint(start.AddHours(i), p)).ToList();

        var moves = PriceMoves.Detect(points, TimeSpan.FromHours(1), 0.05);

        Assert.Equal(2, moves.Count);
        Assert.True(moves[0].ToPrice < moves[0].FromPrice);
        Assert.True(moves[1].ToPrice > moves[1].FromPrice);
    }

    [Fact]
    public void The_real_week_of_liverpool_prices_reads_as_points()
    {
        var history = PolymarketFixtures.Read<ClobPriceHistory>("history-liv-yes-1w");

        Assert.Equal(153, history.History!.Count);
        Assert.All(history.History, point => Assert.InRange(point.P, 0.0, 1.0));
    }
}
