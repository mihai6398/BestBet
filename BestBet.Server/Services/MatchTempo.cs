namespace BestBet.Services;

/// <summary>
/// How the scoring rate rises through a match, as a weighting on its minutes.
/// </summary>
/// <remarks>
/// The pace model measures a rate per minute and runs it forward per minute, as if every minute of a
/// match were worth the same. They are not. Goals come slowest in the opening quarter of an hour and
/// fastest in the last: about 45% of them before half-time and 55% after is the split usually
/// reported for the big leagues, and goals per fifteen minutes climb, with small wobbles, from the
/// first block to the last. Fatigue, substitutions and sides chasing a result all push the same way.
///
/// Read flat, that has a direction. The xG on the clock at minute 23 was produced in the quietest part
/// of the match, and the 75 minutes left include the liveliest — so the flat rate under-states the
/// rest of the match, every match, always on the side of the Under. Early in a match that is exactly
/// when the model looks most confident about a low total.
///
/// The correction is a straight line: a minute at kick-off is worth <c>1 − Swing</c> of an average
/// minute, a minute at the final whistle <c>1 + Swing</c>, and the average over a match is exactly
/// one, so a per-match expectation is unchanged by it. At 0.15 it puts 45.5% of goals before
/// half-time, stoppage included. It does not try to follow the dip at the start of the second half; a
/// line that is right on average is worth more here than a curve with more numbers to argue about.
/// The conservative end on purpose: part of the late climb is sides chasing a result, which the
/// score-state correction already counts.
///
/// Like the rest of the pace model, the number is a calibrated heuristic rather than a fitted
/// parameter, stated as a constant so it can be argued with.
/// </remarks>
public static class MatchTempo
{
    /// <summary>
    /// How far a minute at either end of the match is from an average one. 0.15 makes the last minute
    /// worth about a third more than the first.
    /// </summary>
    public const double Swing = 0.15;

    /// <summary>
    /// Average-minute equivalents from kick-off to <paramref name="minute"/>, on a match lasting
    /// <paramref name="matchMinutes"/>.
    /// </summary>
    /// <remarks>
    /// Past the end of a typical match — a long stoppage — every further minute is worth as much as
    /// the last one, rather than the line carrying on climbing into minutes it was never drawn for.
    /// </remarks>
    public static double Cumulative(double minute, double matchMinutes)
    {
        if (minute <= 0.0)
        {
            return 0.0;
        }

        if (minute >= matchMinutes)
        {
            return matchMinutes + (1.0 + Swing) * (minute - matchMinutes);
        }

        return (1.0 - Swing) * minute + Swing * minute * minute / matchMinutes;
    }

    /// <summary>
    /// The minute by which <paramref name="averageMinutes"/> average minutes' worth of football has
    /// been played: the inverse of <see cref="Cumulative"/>.
    /// </summary>
    public static double MinuteAt(double averageMinutes, double matchMinutes)
    {
        if (averageMinutes <= 0.0)
        {
            return 0.0;
        }

        if (averageMinutes >= matchMinutes)
        {
            return matchMinutes + (averageMinutes - matchMinutes) / (1.0 + Swing);
        }

        // Swing·t²/M + (1 − Swing)·t − x = 0, the positive root.
        var a = Swing / matchMinutes;
        var b = 1.0 - Swing;
        return (-b + Math.Sqrt(b * b + 4.0 * a * averageMinutes)) / (2.0 * a);
    }

    /// <summary>Average-minute equivalents between two points of the match.</summary>
    public static double Minutes(double from, double to, double matchMinutes)
        => Cumulative(to, matchMinutes) - Cumulative(Math.Max(0.0, from), matchMinutes);

    /// <summary>
    /// How much livelier than an average minute the stretch between two points is: below one for the
    /// opening of a match, above one for its end.
    /// </summary>
    public static double Intensity(double from, double to, double matchMinutes)
        => to <= from ? 1.0 : Minutes(from, to, matchMinutes) / (to - from);
}
