using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Position sizing. Full Kelly is correct only if the probability estimate is correct, and it never
/// is, so this stakes a quarter of it and then clamps hard into a 3–5% band.
/// </summary>
public static class KellyStaking
{
    /// <summary>Kelly is divided by this before any clamping — the usual defence against estimate error.</summary>
    public const double KellyFraction = 0.25;

    /// <summary>Anything staked at all is staked at least this fraction of bankroll.</summary>
    public const double MinimumStakeFraction = 0.03;

    /// <summary>No single position ever exceeds this fraction of bankroll, whatever Kelly says.</summary>
    public const double MaximumStakeFraction = 0.05;

    /// <summary>
    /// Unclamped Kelly fraction: (b * p - q) / b, with b = decimalOdds - 1.
    /// Negative when the price does not cover the estimate.
    /// </summary>
    public static double FullKelly(double ownProbability, double decimalOdds)
    {
        var b = decimalOdds - 1.0;
        if (b <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalOdds),
                decimalOdds,
                "Decimal odds must be greater than 1.0.");
        }

        return (b * ownProbability - (1.0 - ownProbability)) / b;
    }

    /// <summary>
    /// Quarter-Kelly, clamped into the 3–5% band. Returns a zero stake when the edge is not positive:
    /// a bet with no edge is not a small bet, it is no bet.
    /// </summary>
    public static StakeRecommendation RecommendStake(double ownProbability, double decimalOdds, double bankroll)
    {
        var fullKelly = FullKelly(ownProbability, decimalOdds);
        if (fullKelly <= 0.0)
        {
            return new StakeRecommendation(0.0, 0.0, "no-positive-edge");
        }

        var quarterKelly = fullKelly * KellyFraction;

        double stakeFraction;
        string capReason;
        if (quarterKelly < MinimumStakeFraction)
        {
            stakeFraction = MinimumStakeFraction;
            capReason = "floor-3pct";
        }
        else if (quarterKelly > MaximumStakeFraction)
        {
            stakeFraction = MaximumStakeFraction;
            capReason = "cap-5pct";
        }
        else
        {
            stakeFraction = quarterKelly;
            capReason = "quarter-kelly";
        }

        return new StakeRecommendation(stakeFraction, stakeFraction * bankroll, capReason);
    }

    /// <summary>Zero stake, tagged with the reason the verdict engine rejected the bet.</summary>
    public static StakeRecommendation NoStake(string capReason) => new(0.0, 0.0, capReason);
}
