namespace BestBet.Domain;

/// <summary>
/// What the pace model did to the raw rate, and why — shown to the user rather than applied silently.
/// A number that quietly halves a probability has to be able to account for itself.
/// </summary>
/// <param name="RawRateHome">Home xG per minute, as a flat average over the whole match so far.</param>
/// <param name="RawRateAway">Away xG per minute, flat average.</param>
/// <param name="AdjustedRateHome">Home xG per minute after every enabled adjustment.</param>
/// <param name="AdjustedRateAway">Away xG per minute after every enabled adjustment.</param>
/// <param name="ProjectedFullMatchXgHome">What the RAW rate implies over ninety minutes. This is the
/// sanity check: a number well above 3 means the flat extrapolation has left reality behind.</param>
/// <param name="ProjectedFullMatchXgAway">The same for the away side.</param>
/// <param name="IsAdjusted">False when the raw rate was used untouched.</param>
/// <param name="Steps">One line per adjustment that actually fired, in the order they were applied.</param>
/// <param name="Warning">Set when the raw projection was implausible enough to be worth saying out loud.</param>
/// <param name="ElapsedMinutes">Minutes of play the rates were measured over: the minute on the clock,
/// plus the first half's stoppage once the second half is on, when stoppage is counted.</param>
/// <param name="RemainingMinutes">Minutes of play the rates are run over to the end of regulation time.</param>
/// <param name="PriorGoalsHome">Goals per match the home rate was pulled towards. Null when shrinkage
/// did not run, because then nothing was pulled anywhere.</param>
/// <param name="PriorGoalsAway">The same for the away side.</param>
/// <param name="PriorFromPreMatch">True when those came from this match's pre-match model rather than
/// from the league average. The two read very differently: one says "a typical match", the other
/// says "what these two teams usually produce".</param>
public record PaceAdjustment(
    double RawRateHome,
    double RawRateAway,
    double AdjustedRateHome,
    double AdjustedRateAway,
    double ProjectedFullMatchXgHome,
    double ProjectedFullMatchXgAway,
    bool IsAdjusted,
    IReadOnlyList<string> Steps,
    string? Warning,
    double ElapsedMinutes,
    double RemainingMinutes,
    double? PriorGoalsHome = null,
    double? PriorGoalsAway = null,
    bool PriorFromPreMatch = false)
{
    /// <summary>Combined raw rate, per minute.</summary>
    public double RawRateTotal => RawRateHome + RawRateAway;

    /// <summary>Combined adjusted rate, per minute.</summary>
    public double AdjustedRateTotal => AdjustedRateHome + AdjustedRateAway;

    /// <summary>
    /// Which way the correction went. Below 1 the model has pulled the pace DOWN (a hot start it does
    /// not believe will hold); above 1 it has pulled it UP (a quiet spell it expects to break).
    /// </summary>
    public double PaceMultiplier => RawRateTotal <= 0 ? 1.0 : AdjustedRateTotal / RawRateTotal;
}
