namespace BestBet.Domain;

/// <param name="OwnProbability">0..1, the user's/model's estimate for the exact outcome being priced.</param>
/// <param name="Method">"poisson-live-xg" | "manual-input" | "external-consensus".</param>
/// <param name="Notes">Free text shown to the user: how it was derived, and any caveats that apply.</param>
public record ProbabilityEstimate(
    double OwnProbability,
    string Method,
    string? Notes);
