using BestBet.Domain;

namespace BestBet.Data.Entities;

/// <summary>Where a bet stands. Everything but <see cref="Open"/> is a settled bet.</summary>
public enum BetStatus
{
    /// <summary>Not settled yet.</summary>
    Open,
    Won,
    Lost,
    /// <summary>Stake returned — a push on a whole line, or a voided market.</summary>
    Void,
    /// <summary>Half the stake won at the price and half returned — a quarter line landing on its upper half.</summary>
    HalfWon,
    /// <summary>Half the stake lost and half returned — a quarter line landing on its lower half.</summary>
    HalfLost,
    /// <summary>Closed early at whatever the bookmaker offered; the payout is what decides the result.</summary>
    CashedOut,
}

/// <summary>
/// One bet a user saved, kept so the strategy can be judged on closing-line value rather than on how
/// the evening felt.
/// </summary>
/// <remarks>
/// Every row belongs to exactly one user, and every query in the app reads through that owner: a bet
/// that is not yours does not exist as far as the API is concerned.
///
/// Money and prices are <see cref="decimal"/> and stored as <c>numeric</c>, so a stake of 3.88 stays
/// 3.88 through every sum the balance and the statistics are built from. The model's outputs —
/// probabilities, edges, xG — stay <see cref="double"/>, because they are estimates and the maths that
/// produces them is floating-point anyway.
///
/// The field that matters most is still <see cref="ClosingDecimalOdds"/>. Win rate over a handful of
/// bets is noise; beating the closing price is the only signal that shows up fast enough to be worth
/// measuring. Everything else in this row exists to give that number context.
/// </remarks>
public class Bet
{
    public int Id { get; set; }

    /// <summary>The owner. Required, indexed, and the first thing every query filters on.</summary>
    public Guid UserId { get; set; }

    /// <summary>When the bet was placed (saved), always UTC.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // --- What was bet -------------------------------------------------------

    /// <summary>e.g. "Fotbal". Optional: most rows come from the football model.</summary>
    public string? Sport { get; set; }

    /// <summary>e.g. "Bundesliga".</summary>
    public string? Competition { get; set; }

    /// <summary>The event, free text, e.g. "Sunderland - Arsenal".</summary>
    public string MatchLabel { get; set; } = string.Empty;

    /// <summary>Kick-off, UTC, when the user gave one.</summary>
    public DateTime? EventStartUtc { get; set; }

    public MarketKind MarketKind { get; set; }

    /// <summary>The bookmaker's own wording for the outcome, e.g. "Primul gol: 2".</summary>
    public string MarketLabel { get; set; } = string.Empty;

    /// <summary>Resolved side of the market, e.g. "NextGoalAway" or "Under 3.5".</summary>
    public string Selection { get; set; } = string.Empty;

    /// <summary>Where it was placed, e.g. "Superbet".</summary>
    public string? Bookmaker { get; set; }

    // --- The price taken ----------------------------------------------------

    public decimal EntryDecimalOdds { get; set; }

    /// <summary>Sibling prices at entry, comma-separated, so the entry de-vig can be re-derived.</summary>
    public string EntrySiblingOddsCsv { get; set; } = string.Empty;

    public double EntryFairProbability { get; set; }

    /// <summary>The estimated probability at entry — the model's, or the user's own.</summary>
    public double EntryOwnProbability { get; set; }

    /// <summary>Own probability minus fair probability, in percentage points.</summary>
    public double EntryEdgePercentagePoints { get; set; }

    public string EstimateMethod { get; set; } = string.Empty;

    // --- What was staked ----------------------------------------------------

    public decimal BankrollAtEntry { get; set; }

    public decimal StakeAmount { get; set; }

    public double StakeFraction { get; set; }

    public Verdict Verdict { get; set; }

    // --- Live snapshot, when there was one ----------------------------------

    public bool WasLive { get; set; }

    public int? MinutePlayed { get; set; }

    public double? CumulativeXgHome { get; set; }

    public double? CumulativeXgAway { get; set; }

    public int? GoalsHome { get; set; }

    public int? GoalsAway { get; set; }

    // --- Filled in later ----------------------------------------------------

    /// <summary>
    /// The price this outcome closed at. Typed in by the user before kick-off or at full time;
    /// automating this capture is Phase 2.
    /// </summary>
    public decimal? ClosingDecimalOdds { get; set; }

    /// <summary>Sibling prices at the close, comma-separated, so the close can be de-vigged too.</summary>
    public string? ClosingSiblingOddsCsv { get; set; }

    public BetStatus Status { get; set; } = BetStatus.Open;

    /// <summary>What came back, stake included. Null while the bet is open.</summary>
    public decimal? Payout { get; set; }

    /// <summary>Payout minus stake. Null while the bet is open.</summary>
    public decimal? ProfitLoss { get; set; }

    /// <summary>When it was settled, UTC. Null while the bet is open.</summary>
    public DateTime? SettledUtc { get; set; }

    public string? Notes { get; set; }
}
