namespace BestBet.Domain;

/// <param name="MinutePlayed">Minutes elapsed, 1..90+. Ignored when <paramref name="IsPreMatch"/>.</param>
/// <param name="CumulativeXgHome">Total expected goals accrued by the home team so far.</param>
/// <param name="CumulativeXgAway">Total expected goals accrued by the away team so far.</param>
/// <param name="CurrentGoalsHome">Goals actually scored by the home team so far.</param>
/// <param name="CurrentGoalsAway">Goals actually scored by the away team so far.</param>
/// <param name="IsPreMatch">true =&gt; every other field is ignored and the caller must supply
/// their own probability manually, because there is no live data to derive one from.</param>
/// <param name="Period">Which half the clock is in. Null means "work it out from the minute", which
/// is right everywhere except first-half stoppage — see <see cref="MatchPeriod"/>.</param>
/// <param name="AddedTimeFirstHalf">Minutes the fourth official's board showed at 45. Null until it
/// has been shown, in which case the model counts the stoppage referees add on average.</param>
/// <param name="AddedTimeSecondHalf">Minutes the board showed at 90. Null until it has been shown.</param>
/// <param name="RedCardsHome">Home players sent off so far, second yellows included.</param>
/// <param name="RedCardsAway">Away players sent off so far.</param>
/// <param name="LastRedCardMinute">When the most recent of those happened. It decides how much of the
/// xG already on the clock was produced with a man down; null is read as "just now".</param>
public record LiveMatchState(
    int MinutePlayed,
    double CumulativeXgHome,
    double CumulativeXgAway,
    int CurrentGoalsHome,
    int CurrentGoalsAway,
    bool IsPreMatch,
    MatchPeriod? Period = null,
    int? AddedTimeFirstHalf = null,
    int? AddedTimeSecondHalf = null,
    int RedCardsHome = 0,
    int RedCardsAway = 0,
    int? LastRedCardMinute = null);
