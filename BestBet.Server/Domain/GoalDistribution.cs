namespace BestBet.Domain;

/// <summary>
/// The distribution of goals still to come, produced by <c>PoissonGoalModel</c>.
/// Home and away are modelled as two independent Poisson processes; the PMF arrays are
/// truncated at <see cref="MaxGoals"/>, by which point the residual mass is negligible.
/// </summary>
/// <param name="RemainingMinutes">Minutes the model extrapolated over, stoppage time included.</param>
/// <param name="ExpectedRemainingHome">Lambda for home goals in the remaining time.</param>
/// <param name="ExpectedRemainingAway">Lambda for away goals in the remaining time.</param>
/// <param name="RemainingHomePmf">Index k holds P(home scores exactly k more goals).</param>
/// <param name="RemainingAwayPmf">Index k holds P(away scores exactly k more goals).</param>
/// <param name="CurrentGoalsHome">Goals already on the board for home, carried so final totals can be formed.</param>
/// <param name="CurrentGoalsAway">Goals already on the board for away.</param>
public record GoalDistribution(
    int RemainingMinutes,
    double ExpectedRemainingHome,
    double ExpectedRemainingAway,
    double[] RemainingHomePmf,
    double[] RemainingAwayPmf,
    int CurrentGoalsHome,
    int CurrentGoalsAway)
{
    /// <summary>Highest remaining-goal count the PMF arrays carry.</summary>
    public int MaxGoals => RemainingHomePmf.Length - 1;

    /// <summary>P(home ends on exactly <paramref name="goals"/>), current score included.</summary>
    public double FinalHomeGoalsProbability(int goals)
    {
        var remaining = goals - CurrentGoalsHome;
        return remaining < 0 || remaining > MaxGoals ? 0.0 : RemainingHomePmf[remaining];
    }

    /// <summary>P(away ends on exactly <paramref name="goals"/>), current score included.</summary>
    public double FinalAwayGoalsProbability(int goals)
    {
        var remaining = goals - CurrentGoalsAway;
        return remaining < 0 || remaining > MaxGoals ? 0.0 : RemainingAwayPmf[remaining];
    }

    /// <summary>
    /// Convolution of the two independent processes: index k holds P(exactly k MORE goals in total).
    /// </summary>
    public double[] RemainingTotalPmf()
    {
        var result = new double[MaxGoals + 1];
        for (var home = 0; home <= MaxGoals; home++)
        {
            for (var away = 0; home + away <= MaxGoals; away++)
            {
                result[home + away] += RemainingHomePmf[home] * RemainingAwayPmf[away];
            }
        }

        return result;
    }

    /// <summary>
    /// Index k holds P(the match finishes on exactly k goals in total). Offset by the current score,
    /// so the first <c>CurrentGoalsHome + CurrentGoalsAway</c> entries are zero by construction.
    /// </summary>
    public double[] FinalTotalPmf()
    {
        var alreadyScored = CurrentGoalsHome + CurrentGoalsAway;
        var remaining = RemainingTotalPmf();
        var result = new double[alreadyScored + remaining.Length];
        for (var k = 0; k < remaining.Length; k++)
        {
            result[alreadyScored + k] = remaining[k];
        }

        return result;
    }
}
