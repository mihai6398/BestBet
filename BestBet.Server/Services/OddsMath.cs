namespace BestBet.Services;

/// <summary>
/// Odds arithmetic. Everything here is pure: same inputs, same outputs, no state.
/// </summary>
public static class OddsMath
{
    /// <summary>
    /// Raw probability implied by a decimal price, vig included. 1.57 =&gt; 0.6369.
    /// This is NOT a fair probability — bookmaker margin is still baked in.
    /// </summary>
    public static double ImpliedProbability(double decimalOdds)
    {
        if (decimalOdds <= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalOdds),
                decimalOdds,
                "Decimal odds must be greater than 1.0 (1.0 would be a free bet with no return).");
        }

        return 1.0 / decimalOdds;
    }

    /// <summary>
    /// Sum of implied probabilities across a complete market. 1.0 would be a margin-free book;
    /// real books come in above it (1.2393 on the 1.57 / 1.66 example = 23.9% overround).
    /// </summary>
    public static double Overround(double oddsForOutcome, IReadOnlyList<double> siblingOdds)
    {
        var total = ImpliedProbability(oddsForOutcome);
        foreach (var sibling in siblingOdds)
        {
            total += ImpliedProbability(sibling);
        }

        return total;
    }

    /// <summary>
    /// De-vig by proportional normalisation. Two-way and three-way markets reduce to the same thing:
    /// sum the implied probability of every outcome in the market, then divide this outcome's share
    /// by that sum.
    /// </summary>
    /// <remarks>
    /// Proportional (a.k.a. multiplicative) de-vig is the standard quick method, and it is what was
    /// used by hand. It slightly over-taxes favourites versus a power or Shin de-vig, which matters
    /// on lopsided markets (odds-on shots below ~1.20). Documented rather than hidden.
    /// </remarks>
    public static double DeVig(double oddsForOutcome, IReadOnlyList<double> siblingOdds)
    {
        if (siblingOdds.Count == 0)
        {
            throw new ArgumentException(
                "De-vig needs at least one sibling outcome; a single price carries no information " +
                "about the bookmaker's margin.",
                nameof(siblingOdds));
        }

        return ImpliedProbability(oddsForOutcome) / Overround(oddsForOutcome, siblingOdds);
    }

    /// <summary>
    /// Expected profit per 1 unit staked, at the price actually on offer.
    /// Positive means the bet is worth taking on its own terms.
    /// </summary>
    public static double ExpectedValuePerUnit(double ownProbability, double decimalOdds)
        => ownProbability * decimalOdds - 1.0;
}
