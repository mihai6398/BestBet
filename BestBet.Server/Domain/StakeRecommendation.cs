namespace BestBet.Domain;

/// <param name="SuggestedStakeFraction">Fraction of bankroll: 0, or clamped into 0.03..0.05.</param>
/// <param name="SuggestedStakeAmount">SuggestedStakeFraction * bankroll.</param>
/// <param name="CapReason">Which rule produced the number: "quarter-kelly", "floor-3pct",
/// "cap-5pct", "no-positive-edge", "skip-below-threshold".</param>
public record StakeRecommendation(
    double SuggestedStakeFraction,
    double SuggestedStakeAmount,
    string CapReason);
