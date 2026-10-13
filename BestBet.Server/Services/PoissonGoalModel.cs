using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Turns "0.79 xG in 67 minutes" into a probability distribution over the goals still to come.
/// This is the core of the app: every live probability estimate comes out of here.
/// </summary>
public static class PoissonGoalModel
{
    /// <summary>Regulation time. Stoppage is handled separately.</summary>
    public const int RegulationMinutes = 90;

    /// <summary>Past this minute the clock is effectively into stoppage time already.</summary>
    public const int StoppageStartsAfterMinute = 85;

    /// <summary>Minutes of stoppage assumed once past <see cref="StoppageStartsAfterMinute"/>.</summary>
    public const int AssumedStoppageMinutes = 5;

    /// <summary>
    /// The honest health warning that belongs on every live result, in the UI and not just in a comment.
    /// </summary>
    public const string PaceCaveat =
        "Assumes current pace holds. Real matches can suddenly open up — treat this as a floor, " +
        "not a ceiling, on scoring probability.";

    /// <summary>
    /// Extrapolates each team's xG rate over the minutes still to be played and returns the resulting
    /// independent-Poisson distribution of remaining goals.
    /// </summary>
    /// <remarks>
    /// KNOWN LIMITATION, stated plainly rather than buried: this assumes a constant scoring rate for
    /// the rest of the match. It systematically UNDERESTIMATES the chance of a sudden burst after a
    /// long quiet stretch. A match that has sat scoreless for half an hour despite large accumulated
    /// xG is precisely the case this model prices too low — the rate it reads off the clock is an
    /// average over a period that already "should" have produced goals, and it has no term for a game
    /// opening up late. Read its output as a floor on scoring probability, not a point estimate.
    ///
    /// Two further simplifications: home and away are treated as independent (they are not — a goal
    /// changes both teams' behaviour), and a team sitting on 0.00 xG is assigned exactly zero chance
    /// of scoring, which is wrong but only bites in the first few minutes of a match.
    /// </remarks>
    public static GoalDistribution ExtrapolateRemainingGoals(LiveMatchState state)
        => ExtrapolateRemainingGoals(state, PaceSettings.Flat);

    /// <summary>
    /// The same extrapolation, with <see cref="PaceModel"/> allowed to correct the rate first.
    /// Passing <see cref="PaceSettings.Flat"/> reproduces the spec's behaviour exactly.
    /// </summary>
    public static GoalDistribution ExtrapolateRemainingGoals(LiveMatchState state, PaceSettings settings)
        => ExtrapolateFrom(state, PaceModel.Adjust(state, settings));

    /// <summary>
    /// Builds the remaining-goals distribution from rates the pace model has already settled on.
    /// </summary>
    public static GoalDistribution ExtrapolateFrom(LiveMatchState state, PaceAdjustment adjustment)
    {
        var remainingMinutes = RemainingMinutes(state.MinutePlayed);

        var expectedRemainingHome = adjustment.AdjustedRateHome * remainingMinutes;
        var expectedRemainingAway = adjustment.AdjustedRateAway * remainingMinutes;

        // Truncation is driven by the COMBINED rate, not the larger of the two: the convolution that
        // produces total goals drops every pair whose sum overflows the table, so the tail that has to
        // be negligible is the tail of the total.
        var maxGoals = ChooseTruncation(expectedRemainingHome + expectedRemainingAway);

        return new GoalDistribution(
            remainingMinutes,
            expectedRemainingHome,
            expectedRemainingAway,
            PoissonPmfTable(expectedRemainingHome, maxGoals),
            PoissonPmfTable(expectedRemainingAway, maxGoals),
            state.CurrentGoalsHome,
            state.CurrentGoalsAway);
    }

    /// <summary>
    /// Minutes left to play. Past minute 85 a fixed stoppage allowance is added, so a match shown as
    /// "90+2" still has scoring time left in the model rather than collapsing to zero.
    /// </summary>
    public static int RemainingMinutes(int minutePlayed)
    {
        var regulationLeft = Math.Max(0, RegulationMinutes - minutePlayed);
        return minutePlayed > StoppageStartsAfterMinute
            ? regulationLeft + AssumedStoppageMinutes
            : regulationLeft;
    }

    /// <summary>P(exactly k events) for a Poisson process with mean <paramref name="lambda"/>.</summary>
    public static double PoissonPmf(int k, double lambda)
    {
        if (k < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "Goal counts cannot be negative.");
        }

        if (lambda < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lambda), lambda, "Poisson mean cannot be negative.");
        }

        if (lambda == 0.0)
        {
            return k == 0 ? 1.0 : 0.0;
        }

        // Built up term by term (p_k = p_{k-1} * lambda / k) rather than via lambda^k / k!,
        // which overflows the factorial long before the probabilities stop mattering.
        var p = Math.Exp(-lambda);
        for (var i = 1; i <= k; i++)
        {
            p *= lambda / i;
        }

        return p;
    }

    /// <summary>PMF laid out as an array, index k = P(exactly k), truncated at <paramref name="maxGoals"/>.</summary>
    public static double[] PoissonPmfTable(double lambda, int maxGoals)
    {
        var table = new double[maxGoals + 1];
        for (var k = 0; k <= maxGoals; k++)
        {
            table[k] = PoissonPmf(k, lambda);
        }

        return table;
    }

    /// <summary>
    /// Where to cut the PMF off. Ten standard deviations past the mean leaves a residual far below any
    /// rounding the user will ever see. The floor of 15 matters more than it looks: at the low rates
    /// typical of a late-match extrapolation the standard-deviation term is tiny, and a short table
    /// would leave enough mass outside it to show up in a probability quoted to four decimals.
    /// </summary>
    private static int ChooseTruncation(double totalLambda)
    {
        var suggested = (int)Math.Ceiling(totalLambda + 10.0 * Math.Sqrt(totalLambda)) + 1;
        return Math.Clamp(suggested, 15, 40);
    }
}
