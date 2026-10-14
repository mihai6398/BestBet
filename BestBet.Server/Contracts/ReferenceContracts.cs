namespace BestBet.Contracts;

/// <summary>
/// One outcome of a prediction market, with its price read both ways.
/// </summary>
/// <remarks>
/// A prediction market quotes in probability: an outcome trading at 63 cents pays a dollar if it
/// happens, so 63 cents IS the market's 63%. That makes the conversion to decimal odds exact
/// (1 / 0.63 = 1.587) rather than an estimate, and it means a complete market there adds up to
/// roughly 1.00 instead of a bookmaker's 1.05–1.15. Both forms are returned because the user
/// recognises the cents and the rest of this app speaks decimal odds.
/// </remarks>
/// <param name="Label">The outcome as the market names it, e.g. "Yes", "Over".</param>
/// <param name="Probability">0..1, the traded price.</param>
/// <param name="DecimalOdds">The same price as decimal odds, so it can be de-vigged like any book.</param>
public record PredictionOutcome(string Label, double Probability, double DecimalOdds);

/// <summary>
/// A market found on a prediction market exchange, offered as a possible reference line.
/// </summary>
/// <param name="Id">The exchange's own id, used as a stable key by the UI.</param>
/// <param name="Question">The market's question, which is also what says whether it is the right one.</param>
/// <param name="EventTitle">The event the market belongs to, e.g. "Champions League Final".</param>
/// <param name="Url">A link to the market, so the user can check it rather than trust this.</param>
/// <param name="Volume">Traded volume in dollars. A thin market is a worse reference than no reference.</param>
/// <param name="Liquidity">Resting liquidity in dollars, same caveat.</param>
/// <param name="EndDate">When the market resolves, which is how a last-season copy is spotted.</param>
/// <param name="Outcomes">Every outcome, so the fair line can be computed across the whole book.</param>
/// <param name="MarketType">Polymarket's own tag for what kind of market this is, e.g. "moneyline", "totals".</param>
/// <param name="Line">The goal line for totals and spreads (2.5 in "O/U 2.5"); null for markets without one.</param>
public record PredictionMarketDto(
    string Id,
    string Question,
    string EventTitle,
    string? Url,
    double? Volume,
    double? Liquidity,
    DateTime? EndDate,
    IReadOnlyList<PredictionOutcome> Outcomes,
    string? MarketType = null,
    double? Line = null);
