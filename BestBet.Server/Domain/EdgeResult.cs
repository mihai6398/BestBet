namespace BestBet.Domain;

/// <param name="FairMarketProbability">De-vigged probability implied by the market, from the sibling odds.</param>
/// <param name="OwnProbability">The estimate we are comparing against the market.</param>
/// <param name="EdgePercentagePoints">(OwnProbability - FairMarketProbability) * 100.</param>
/// <param name="ExpectedValuePerUnit">OwnProbability * DecimalOdds - 1, at the price actually offered.
/// NOTE: this can be negative while <paramref name="EdgePercentagePoints"/> is positive — that means
/// the estimate beats the fair line but not the vig-inflated price on the screen.</param>
public record EdgeResult(
    double FairMarketProbability,
    double OwnProbability,
    double EdgePercentagePoints,
    double ExpectedValuePerUnit);
