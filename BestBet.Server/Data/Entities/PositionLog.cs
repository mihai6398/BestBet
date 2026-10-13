using BestBet.Domain;

namespace BestBet.Data.Entities;

public enum PositionOutcome
{
    /// <summary>Not settled yet.</summary>
    Pending,
    Won,
    Lost,
    /// <summary>Stake returned — a push on a whole line, or a voided market.</summary>
    Void
}

/// <summary>
/// One saved analysis, kept so the strategy can be judged on closing-line value rather than on
/// how the evening felt.
/// </summary>
/// <remarks>
/// The field that matters most here is <see cref="ClosingDecimalOdds"/>. Win rate over a handful of
/// bets is noise; beating the closing price is the only signal that shows up fast enough to be worth
/// measuring. Everything else in this row exists to give that number context.
/// </remarks>
public class PositionLog
{
    public int Id { get; set; }

    /// <summary>When the analysis was saved, always UTC.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // --- What was bet -------------------------------------------------------

    /// <summary>Free text, e.g. "Sunderland - Arsenal". Purely for the user's own recall.</summary>
    public string MatchLabel { get; set; } = string.Empty;

    public MarketKind MarketKind { get; set; }

    /// <summary>The bookmaker's own wording for the outcome, e.g. "Primul gol: 2".</summary>
    public string MarketLabel { get; set; } = string.Empty;

    /// <summary>Resolved side of the market, e.g. "NextGoalAway" or "Under 3.5".</summary>
    public string Selection { get; set; } = string.Empty;

    // --- The price taken ----------------------------------------------------

    public double EntryDecimalOdds { get; set; }

    /// <summary>Sibling prices at entry, comma-separated, so the entry de-vig can be re-derived.</summary>
    public string EntrySiblingOddsCsv { get; set; } = string.Empty;

    public double EntryFairProbability { get; set; }

    public double EntryOwnProbability { get; set; }

    public double EntryEdgePercentagePoints { get; set; }

    public string EstimateMethod { get; set; } = string.Empty;

    // --- What was staked ----------------------------------------------------

    public double BankrollAtEntry { get; set; }

    public double StakeAmount { get; set; }

    public double StakeFraction { get; set; }

    public Verdict Verdict { get; set; }

    // --- Live snapshot, when there was one ----------------------------------

    public bool WasLive { get; set; }

    public int? MinutePlayed { get; set; }

    public double? CumulativeXgHome { get; set; }

    public double? CumulativeXgAway { get; set; }

    public int? GoalsHome { get; set; }

    public int? GoalsAway { get; set; }

    // --- Filled in later, by hand -------------------------------------------

    /// <summary>
    /// The price this outcome closed at. Typed in by the user before kick-off or at full time;
    /// automating this capture is Phase 2.
    /// </summary>
    public double? ClosingDecimalOdds { get; set; }

    /// <summary>Sibling prices at the close, comma-separated, so the close can be de-vigged too.</summary>
    public string? ClosingSiblingOddsCsv { get; set; }

    public PositionOutcome Outcome { get; set; } = PositionOutcome.Pending;

    public string? Notes { get; set; }
}
