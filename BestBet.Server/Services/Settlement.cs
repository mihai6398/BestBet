using BestBet.Data.Entities;

namespace BestBet.Services;

/// <summary>
/// What a bet pays back once it is settled, and what that leaves as profit or loss.
/// </summary>
/// <remarks>
/// Worked out in <see cref="decimal"/> and rounded to the cent once, at the end, so the payout and the
/// profit always agree with each other to the cent and the balance built from them never drifts.
/// </remarks>
public static class Settlement
{
    /// <param name="status">Any settled status; <see cref="BetStatus.Open"/> has no payout.</param>
    /// <param name="stake">What was staked.</param>
    /// <param name="odds">The decimal price taken.</param>
    /// <param name="cashOutPayout">What the bookmaker paid on a cash-out. Required for
    /// <see cref="BetStatus.CashedOut"/> and ignored for everything else.</param>
    public static (decimal Payout, decimal ProfitLoss) For(
        BetStatus status,
        decimal stake,
        decimal odds,
        decimal? cashOutPayout = null)
    {
        var half = stake / 2m;

        var payout = status switch
        {
            BetStatus.Won => stake * odds,
            BetStatus.Lost => 0m,
            BetStatus.Void => stake,
            BetStatus.HalfWon => half * odds + half,
            BetStatus.HalfLost => half,
            BetStatus.CashedOut => cashOutPayout is >= 0m
                ? cashOutPayout.Value
                : throw new AnalysisInputException(
                    "A cash-out needs the amount the bookmaker paid back, and it cannot be negative."),
            _ => throw new AnalysisInputException("An open bet has nothing to settle."),
        };

        payout = Math.Round(payout, 2, MidpointRounding.AwayFromZero);
        return (payout, payout - stake);
    }
}
