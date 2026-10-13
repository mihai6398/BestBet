namespace BestBet.Services;

/// <summary>
/// Closing-line value: did the price taken beat the price the market settled on?
/// </summary>
/// <remarks>
/// Win/loss over 30 bets is almost entirely variance and tells you nothing about whether the process
/// works. CLV converges far faster: consistently taking prices better than the close is the signature
/// of a real edge, and consistently failing to is the signature of an imaginary one. This is the
/// number the positions log exists to produce.
/// </remarks>
public static class ClvMath
{
    /// <summary>
    /// Value captured versus the close, as a fraction. 2.10 taken on a line that closed at 2.00
    /// gives +5%: the same bet was worth 5% more at the moment it was struck.
    /// </summary>
    public static double? ClvPercent(double entryDecimalOdds, double? closingDecimalOdds)
        => closingDecimalOdds is > 1.0 && entryDecimalOdds > 1.0
            ? entryDecimalOdds / closingDecimalOdds.Value - 1.0
            : null;

    /// <summary>True when the entry price was better (longer) than the closing price.</summary>
    public static bool? BeatTheClose(double entryDecimalOdds, double? closingDecimalOdds)
        => ClvPercent(entryDecimalOdds, closingDecimalOdds) is { } clv ? clv > 0 : null;

    /// <summary>
    /// CLV in probability points, which needs the closing siblings so the close can be de-vigged the
    /// same way the entry was. Negative means the fair probability moved against the bet.
    /// </summary>
    public static double? ClvProbabilityPoints(
        double entryFairProbability,
        double? closingDecimalOdds,
        IReadOnlyList<double>? closingSiblingOdds)
    {
        if (closingDecimalOdds is not > 1.0 || closingSiblingOdds is null || closingSiblingOdds.Count == 0)
        {
            return null;
        }

        if (closingSiblingOdds.Any(o => o <= 1.0))
        {
            return null;
        }

        var closingFair = OddsMath.DeVig(closingDecimalOdds.Value, closingSiblingOdds);
        return (closingFair - entryFairProbability) * 100.0;
    }

    /// <summary>
    /// Parses the comma-separated sibling-odds column. Tolerates blanks and stray spaces; returns an
    /// empty list rather than throwing, because this data is typed in by hand.
    /// </summary>
    public static double[] ParseOddsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.TryParse(
                part,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value) ? value : double.NaN)
            .Where(value => !double.IsNaN(value))
            .ToArray();
    }

    public static string FormatOddsCsv(IEnumerable<double>? odds)
        => odds is null
            ? string.Empty
            : string.Join(",", odds.Select(o => o.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)));
}
