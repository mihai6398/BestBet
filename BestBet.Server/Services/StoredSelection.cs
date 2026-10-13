using System.Globalization;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Reads back the <c>Selection</c> string a saved position carries.
/// </summary>
/// <remarks>
/// The log stores the resolved selection as text — "Over 2.5", "NextGoalAway", "Yes" — because that
/// is what a person reading the table needs to see. Re-pricing an open position needs it back as a
/// <see cref="MarketSelectionSpec"/>, and this is the only place that conversion happens.
///
/// The canonical form is parsed strictly, because the app wrote it and an enum name either matches
/// or it does not; guessing there would be inventing a side of a market for a bet that is already
/// running. Rows written before the canonical form was used fall back to the free-text parser, and
/// a row that neither can read is reported rather than approximated.
/// </remarks>
public static class StoredSelection
{
    public static MarketSelectionSpec Parse(MarketKind kind, string stored)
    {
        var text = (stored ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new AnalysisInputException(
                "This position has no selection recorded, so there is nothing to re-price.");
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Case-sensitive on purpose: "Over" is the enum this app wrote, "over" is somebody's label.
        if (Enum.TryParse<MarketSelection>(parts[0], ignoreCase: false, out var side))
        {
            double? line = parts.Length > 1
                           && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

            return new MarketSelectionSpec(side, line);
        }

        try
        {
            return MarketSelectionParser.Parse(kind, text);
        }
        catch (ArgumentException ex)
        {
            throw new AnalysisInputException(
                $"This position records its selection as \"{text}\", which cannot be read back as a side " +
                $"of a {kind} market, so it cannot be re-priced. {ex.Message}");
        }
    }
}
