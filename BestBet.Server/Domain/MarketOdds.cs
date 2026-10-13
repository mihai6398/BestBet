namespace BestBet.Domain;

public enum MarketKind
{
    Moneyline1X2,
    TotalGoals,
    BothTeamsToScore,
    NextGoal,
    Handicap
}

/// <summary>
/// One priced outcome, plus the other side(s) of the SAME market so the vig can be removed.
/// </summary>
/// <param name="Kind">Which market family this outcome belongs to.</param>
/// <param name="Label">Bookmaker's own wording, e.g. "Sub 4", "Sevilla FC", "Da". Free text; used
/// for display and, when <see cref="AnalyzeRequest.Selection"/> is not supplied, for parsing.</param>
/// <param name="DecimalOdds">European decimal odds for this outcome, e.g. 1.57.</param>
/// <param name="SiblingOdds">Decimal odds of every OTHER outcome in the same market.
/// One entry for a two-way market ([overOdds]), two for a three-way market ([drawOdds, awayOdds]).</param>
public record MarketOdds(
    MarketKind Kind,
    string Label,
    double DecimalOdds,
    double[] SiblingOdds);
