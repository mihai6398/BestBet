using System.Globalization;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// The final BET / MARGINAL / SKIP call, and the sentence explaining it.
/// </summary>
public static class VerdictEngine
{
    /// <summary>Live markets move fast and the model is cruder, so they have to clear a higher bar.</summary>
    public const double LiveEdgeThresholdPp = 5.0;

    /// <summary>Pre-match estimates are more considered, so a smaller edge is actionable.</summary>
    public const double PreMatchEdgeThresholdPp = 3.0;

    /// <summary>How far below the threshold still counts as MARGINAL rather than SKIP.</summary>
    public const double MarginalBandPp = 2.0;

    public static double ThresholdFor(bool isLive) => isLive ? LiveEdgeThresholdPp : PreMatchEdgeThresholdPp;

    public static Verdict Decide(EdgeResult edge, bool isLive)
    {
        var threshold = ThresholdFor(isLive);

        if (edge.EdgePercentagePoints >= threshold)
        {
            return Verdict.Bet;
        }

        if (edge.EdgePercentagePoints >= threshold - MarginalBandPp)
        {
            return Verdict.Marginal;
        }

        return Verdict.Skip;
    }

    /// <summary>
    /// Plain-language reason, assembled from whichever facts actually drove the call. Includes the
    /// edge-vs-EV conflict when it arises, because that case looks like a bug to the user otherwise.
    /// </summary>
    /// <param name="liveCaveat">The health warning to append for a live market. Supplied by the caller
    /// rather than read from a constant, because which way the pace model is wrong depends on what it
    /// did to this particular match. Null falls back to the spec's fixed wording.</param>
    public static string Explain(
        EdgeResult edge,
        Verdict verdict,
        bool isLive,
        StakeRecommendation stake,
        string? liveCaveat = null)
    {
        var threshold = ThresholdFor(isLive);
        var horizon = isLive ? "live" : "pre-match";
        var parts = new List<string>();

        var edgeText = Pp(edge.EdgePercentagePoints);
        var thresholdText = Pp(threshold);

        parts.Add(verdict switch
        {
            Verdict.Bet =>
                $"Edge of {edgeText} clears the {thresholdText} {horizon} threshold.",
            Verdict.Marginal =>
                $"Edge of {edgeText} is inside the marginal band — above {Pp(threshold - MarginalBandPp)} " +
                $"but short of the {thresholdText} {horizon} threshold.",
            _ when edge.EdgePercentagePoints < 0 =>
                $"Own estimate is {Pp(-edge.EdgePercentagePoints)} BELOW the de-vigged market price. " +
                "The market is on the other side of this.",
            _ =>
                $"Edge of {edgeText} is below the {Pp(threshold - MarginalBandPp)} floor for a {horizon} market.",
        });

        // Edge is measured against the fair (de-vigged) line; EV is measured against the price actually
        // offered. They disagree whenever the bookmaker's margin is wider than the edge, and that is the
        // single most confusing output this app produces, so it gets spelled out.
        if (edge.EdgePercentagePoints > 0 && edge.ExpectedValuePerUnit <= 0)
        {
            parts.Add(
                $"But EV at the offered price is {Percent(edge.ExpectedValuePerUnit)}: the estimate beats the " +
                "de-vigged fair line without beating the price on the screen, so the margin eats the whole edge. " +
                "No stake.");
        }
        else if (edge.ExpectedValuePerUnit > 0)
        {
            parts.Add($"EV at the offered price is {Percent(edge.ExpectedValuePerUnit)} per unit staked.");
        }

        if (stake.SuggestedStakeFraction > 0)
        {
            parts.Add($"Stake sized by {stake.CapReason} at {Percent(stake.SuggestedStakeFraction)} of bankroll.");
        }

        if (isLive)
        {
            parts.Add(liveCaveat ?? PoissonGoalModel.PaceCaveat);
        }

        return string.Join(" ", parts);
    }

    private static string Pp(double percentagePoints)
        => percentagePoints.ToString("0.0", CultureInfo.InvariantCulture) + "pp";

    private static string Percent(double fraction)
        => (fraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
