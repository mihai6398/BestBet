namespace BestBet.Domain;

public enum Verdict
{
    Bet,
    Marginal,
    Skip
}

public record AnalysisResult(
    EdgeResult Edge,
    StakeRecommendation Stake,
    Verdict Verdict,
    string VerdictReason);
