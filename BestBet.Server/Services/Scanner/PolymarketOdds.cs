namespace BestBet.Services.Scanner;

/// <summary>Where a quote came from: the order book itself, or Gamma's copy of its top.</summary>
public enum PriceSource
{
    /// <summary>The CLOB order book of this very token, read a few seconds ago.</summary>
    Book,

    /// <summary>
    /// Gamma's <c>bestBid</c> / <c>bestAsk</c>, used when the book could not be read. Those two are for
    /// the market's first token; the second token's are its mirror image (see <see cref="TopOfBook"/>).
    /// </summary>
    Gamma,
}

/// <summary>
/// The taker fee a Polymarket market charges on a buy.
/// </summary>
/// <remarks>
/// Polymarket's fee page gives <c>fee = shares × rate × p × (1 − p)</c>, in USDC, and the market
/// object carries <c>feeSchedule {rate, exponent}</c>. The exponent is read here as applying to the
/// <c>p × (1 − p)</c> term, which is the documented formula when it is 1 — the only value seen on a
/// live market. The fee peaks at 50¢ and vanishes towards either end.
/// </remarks>
public readonly record struct FeeModel(double Rate, double Exponent)
{
    public static FeeModel None => new(0.0, 1.0);

    public bool IsFree => Rate <= 0.0;

    /// <summary>USDC paid per share bought at <paramref name="price"/>.</summary>
    public double PerShare(double price) =>
        IsFree || price <= 0.0 || price >= 1.0 ? 0.0 : Rate * Math.Pow(price * (1.0 - price), Exponent);
}

/// <summary>
/// The best bid and ask of one token, with the shares resting at each.
/// </summary>
/// <param name="Bid">The best price someone will pay for this outcome — what selling it gets.</param>
/// <param name="Ask">The best price someone will sell it at — what backing it costs.</param>
/// <param name="AskSize">Shares at <paramref name="Ask"/>. Null when the quote came from Gamma.</param>
public readonly record struct TopOfBook(
    double? Bid,
    double? BidSize,
    double? Ask,
    double? AskSize,
    PriceSource Source)
{
    public double? Mid => Bid is { } bid && Ask is { } ask ? (bid + ask) / 2.0 : null;

    public double? Spread => Bid is { } bid && Ask is { } ask ? ask - bid : null;

    /// <summary>Best levels of a real book: the highest bid and the lowest ask, whatever order they came in.</summary>
    public static TopOfBook FromBook(ClobBook book)
    {
        var bid = OrderBookView.Clean(book.Bids).MaxBy(level => level.Price);
        var ask = OrderBookView.Clean(book.Asks).MinBy(level => level.Price);

        return new TopOfBook(bid?.Price, bid?.Size, ask?.Price, ask?.Size, PriceSource.Book);
    }

    /// <summary>
    /// Gamma's copy of the top of the book, for the token at <paramref name="outcomeIndex"/>.
    /// </summary>
    /// <remarks>
    /// Gamma only quotes the first token. The second token's book is the first one seen from the
    /// other side — a bid of 0.63 for Over is an offer to sell Under at 0.37, size for size — so its
    /// ask is 1 − the first token's bid, and its bid 1 − the first token's ask. That is still the real
    /// price to back it, unlike 1 − the mid, which ignores the spread.
    /// </remarks>
    public static TopOfBook FromGamma(GammaMarket market, int outcomeIndex)
    {
        var bid = Usable(market.BestBid);
        var ask = Usable(market.BestAsk);

        return outcomeIndex == 0
            ? new TopOfBook(bid, null, ask, null, PriceSource.Gamma)
            : new TopOfBook(ask is { } a ? 1.0 - a : null, null, bid is { } b ? 1.0 - b : null, null, PriceSource.Gamma);
    }

    /// <summary>An empty book shows up as a bid of 0 or an ask of 1, neither of which is a price.</summary>
    private static double? Usable(double? price) => price is > 0.0 and < 1.0 ? price : null;
}

/// <summary>A book's levels, best first on both sides.</summary>
public sealed record OrderBookView(IReadOnlyList<ClobLevel> Bids, IReadOnlyList<ClobLevel> Asks)
{
    public static OrderBookView From(ClobBook book) => new(
        Clean(book.Bids).OrderByDescending(level => level.Price).ToList(),
        Clean(book.Asks).OrderBy(level => level.Price).ToList());

    /// <summary>Only levels that are a price strictly between 0 and 1 with something resting at them.</summary>
    internal static IEnumerable<ClobLevel> Clean(IEnumerable<ClobLevel>? levels) =>
        (levels ?? []).Where(level => level.Price is > 0.0 and < 1.0 && level.Size > 0.0);
}

/// <summary>
/// Prices to bookmaker odds. Everything here is pure.
/// </summary>
/// <remarks>
/// A share pays $1 if the outcome happens, so its price is the implied probability and 1 / price is
/// the decimal odds. The price that matters for a bet is the ask — what backing actually costs —
/// which is why every "back" number in the scanner is computed from it and the mid is kept as a
/// second, informative column.
/// </remarks>
public static class PolymarketOdds
{
    /// <summary>1 / price. Null for anything that is not a price strictly between 0 and 1.</summary>
    public static double? DecimalOdds(double? price) =>
        price is > 0.0 and < 1.0 ? 1.0 / price.Value : null;

    /// <summary>
    /// What the bet really pays per unit staked once the taker fee is added to the price: each share
    /// costs <c>p + fee(p)</c> and still pays $1.
    /// </summary>
    public static double? NetDecimalOdds(double? price, FeeModel fee) =>
        price is > 0.0 and < 1.0 ? 1.0 / (price.Value + fee.PerShare(price.Value)) : null;

    /// <summary>
    /// Sum of the implied probabilities of every outcome at the prices you would back them at. 1.00 is
    /// a market with no margin; a bookmaker's 1X2 runs 1.05–1.10. Null when any outcome has no ask,
    /// because a sum with a missing term is not a smaller overround, it is no overround.
    /// </summary>
    public static double? Overround(IReadOnlyList<double?> backPrices) =>
        backPrices.Count >= 2 && backPrices.All(p => p is > 0.0 and < 1.0)
            ? backPrices.Sum(p => p!.Value)
            : null;

    /// <summary>
    /// Proportional de-vig of the back prices — the same normalisation <see cref="OddsMath.DeVig"/>
    /// applies to a bookmaker's odds, so an edge here means what it means everywhere else in the app.
    /// </summary>
    public static IReadOnlyList<double?> FairProbabilities(IReadOnlyList<double?> backPrices)
    {
        if (Overround(backPrices) is not { } total)
        {
            return backPrices.Select(_ => (double?)null).ToList();
        }

        return backPrices.Select(p => (double?)(p!.Value / total)).ToList();
    }

    /// <summary>
    /// Dollars that can be put on at the best ask without moving the price: the shares resting there
    /// times what each costs.
    /// </summary>
    public static double? LiquidityAtPrice(double? price, double? shares) =>
        price is > 0.0 and < 1.0 && shares is > 0.0 ? price.Value * shares.Value : null;
}
