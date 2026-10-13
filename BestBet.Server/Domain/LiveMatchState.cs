namespace BestBet.Domain;

/// <param name="MinutePlayed">Minutes elapsed, 1..90+. Ignored when <paramref name="IsPreMatch"/>.</param>
/// <param name="CumulativeXgHome">Total expected goals accrued by the home team so far.</param>
/// <param name="CumulativeXgAway">Total expected goals accrued by the away team so far.</param>
/// <param name="CurrentGoalsHome">Goals actually scored by the home team so far.</param>
/// <param name="CurrentGoalsAway">Goals actually scored by the away team so far.</param>
/// <param name="IsPreMatch">true =&gt; every other field is ignored and the caller must supply
/// their own probability manually, because there is no live data to derive one from.</param>
public record LiveMatchState(
    int MinutePlayed,
    double CumulativeXgHome,
    double CumulativeXgAway,
    int CurrentGoalsHome,
    int CurrentGoalsAway,
    bool IsPreMatch);
