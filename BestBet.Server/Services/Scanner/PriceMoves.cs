using BestBet.Contracts;

namespace BestBet.Services.Scanner;

/// <summary>
/// Finds the stretches of a price history where the market moved a lot in a short time.
/// </summary>
/// <remarks>
/// Each point is compared with the oldest point still inside the window behind it. A move that
/// carries on in the same direction is extended rather than reported again, so one steady slide
/// reads as one marker on the chart instead of a row of them.
/// </remarks>
public static class PriceMoves
{
    public static IReadOnlyList<PriceMove> Detect(
        IReadOnlyList<PricePoint> points,
        TimeSpan window,
        double threshold)
    {
        var moves = new List<PriceMove>();
        if (points.Count < 2 || threshold <= 0.0)
        {
            return moves;
        }

        var ordered = points.OrderBy(p => p.Time).ToList();
        var start = 0;
        var lastEnd = -1;
        var lastDirection = 0;

        for (var i = 1; i < ordered.Count; i++)
        {
            while (ordered[i].Time - ordered[start].Time > window)
            {
                start++;
            }

            if (start == i)
            {
                continue;
            }

            var delta = ordered[i].Price - ordered[start].Price;
            if (Math.Abs(delta) < threshold - 1e-12)
            {
                continue;
            }

            var direction = Math.Sign(delta);
            if (moves.Count > 0 && direction == lastDirection && start <= lastEnd)
            {
                var last = moves[^1];
                moves[^1] = last with { To = ordered[i].Time, ToPrice = ordered[i].Price };
            }
            else
            {
                moves.Add(new PriceMove(ordered[start].Time, ordered[i].Time, ordered[start].Price, ordered[i].Price));
            }

            lastEnd = i;
            lastDirection = direction;
        }

        return moves;
    }
}
