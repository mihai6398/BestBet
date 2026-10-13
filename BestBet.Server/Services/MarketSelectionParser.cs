using System.Globalization;
using System.Text.RegularExpressions;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Best-effort reading of a bookmaker's free-text label into a <see cref="MarketSelectionSpec"/>.
/// </summary>
/// <remarks>
/// This is a FALLBACK, not the primary path. Labels are whatever the site happened to print, in
/// whatever language ("Sub 4", "Peste 2.5", "Da", "Primul gol: 2", "Sevilla FC"), and no parser can
/// be trusted with a decision that sizes a stake. The UI always sends the selection explicitly; this
/// exists so an API call typed by hand still works, and it throws a legible error rather than
/// guessing when the label is genuinely ambiguous.
/// </remarks>
public static partial class MarketSelectionParser
{
    public static MarketSelectionSpec Parse(MarketKind kind, string label)
    {
        var normalised = Normalise(label);

        return kind switch
        {
            MarketKind.TotalGoals => ParseTotalGoals(label, normalised),
            MarketKind.BothTeamsToScore => ParseBtts(label, normalised),
            MarketKind.NextGoal => ParseNextGoal(label, normalised),
            MarketKind.Moneyline1X2 => ParseMoneyline(label, normalised),
            MarketKind.Handicap => ParseHandicap(label, normalised),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown market kind."),
        };
    }

    private static MarketSelectionSpec ParseTotalGoals(string label, string normalised)
    {
        var over = ContainsWord(normalised, "peste", "over", "mai mult") || normalised.EndsWith('+');
        var under = ContainsWord(normalised, "sub", "under", "mai putin") || normalised.EndsWith('-');

        if (over == under)
        {
            throw Ambiguous(label, "expected an over/under word such as \"Sub 3.5\", \"Peste 2.5\" or \"Over 4\"");
        }

        var line = FirstNumber(normalised)
                   ?? throw Ambiguous(label, "no goal line found — expected something like \"Sub 3.5\"");

        return new MarketSelectionSpec(over ? MarketSelection.Over : MarketSelection.Under, line);
    }

    private static MarketSelectionSpec ParseBtts(string label, string normalised)
    {
        if (ContainsWord(normalised, "da", "yes", "gg", "ambele")) return new MarketSelectionSpec(MarketSelection.Yes, null);
        if (ContainsWord(normalised, "nu", "no", "ng")) return new MarketSelectionSpec(MarketSelection.No, null);
        throw Ambiguous(label, "expected \"Da\"/\"Yes\" or \"Nu\"/\"No\"");
    }

    private static MarketSelectionSpec ParseNextGoal(string label, string normalised)
    {
        if (ContainsWord(normalised, "niciun", "niciunul", "none", "fara", "no goal")) return new MarketSelectionSpec(MarketSelection.NextGoalNone, null);
        if (ContainsWord(normalised, "gazde", "gazda", "home")) return new MarketSelectionSpec(MarketSelection.NextGoalHome, null);
        if (ContainsWord(normalised, "oaspeti", "oaspete", "away")) return new MarketSelectionSpec(MarketSelection.NextGoalAway, null);

        // "Primul gol: 2" — the trailing index is the team, 0 meaning no further goal.
        var index = LastNumber(normalised);
        return index switch
        {
            1 => new MarketSelectionSpec(MarketSelection.NextGoalHome, null),
            2 => new MarketSelectionSpec(MarketSelection.NextGoalAway, null),
            0 => new MarketSelectionSpec(MarketSelection.NextGoalNone, null),
            _ => throw Ambiguous(label, "expected \"1\"/\"2\"/\"0\" or a home/away/none word"),
        };
    }

    private static MarketSelectionSpec ParseMoneyline(string label, string normalised)
    {
        if (ContainsWord(normalised, "x", "egal", "remiza", "draw")) return new MarketSelectionSpec(MarketSelection.Draw, null);
        if (ContainsWord(normalised, "gazde", "gazda", "home")) return new MarketSelectionSpec(MarketSelection.Home, null);
        if (ContainsWord(normalised, "oaspeti", "oaspete", "away")) return new MarketSelectionSpec(MarketSelection.Away, null);

        return LastNumber(normalised) switch
        {
            1 => new MarketSelectionSpec(MarketSelection.Home, null),
            2 => new MarketSelectionSpec(MarketSelection.Away, null),
            // A bare team name ("Sevilla FC") is unparseable by design: the model knows "home" and
            // "away", not club names, so the caller has to say which side this is.
            _ => throw Ambiguous(label, "a team name cannot be resolved to a side — send selection explicitly as Home, Draw or Away"),
        };
    }

    private static MarketSelectionSpec ParseHandicap(string label, string normalised)
    {
        MarketSelection side;
        if (ContainsWord(normalised, "gazde", "gazda", "home")) side = MarketSelection.HandicapHome;
        else if (ContainsWord(normalised, "oaspeti", "oaspete", "away")) side = MarketSelection.HandicapAway;
        else
        {
            throw Ambiguous(label, "expected a home/away word, e.g. \"Home -1.5\"");
        }

        var line = FirstNumber(normalised)
                   ?? throw Ambiguous(label, "no handicap line found — expected something like \"Home -1.5\"");

        return new MarketSelectionSpec(side, line);
    }

    private static string Normalise(string label)
    {
        var lowered = (label ?? string.Empty).Trim().ToLowerInvariant();
        // Fold the Romanian diacritics a bookmaker may or may not print, so "oaspeți" and
        // "oaspeti" are the same token.
        return lowered
            .Replace('ă', 'a').Replace('â', 'a').Replace('î', 'i')
            .Replace('ș', 's').Replace('ş', 's')
            .Replace('ț', 't').Replace('ţ', 't');
    }

    /// <summary>
    /// Whole-token match, so "sub" does not fire on "substitution" and "nu" does not fire on "numar".
    /// Multi-word entries ("mai mult") fall back to a substring test, which is safe for phrases.
    /// </summary>
    private static bool ContainsWord(string normalised, params string[] words)
    {
        var tokens = TokenRegex().Matches(normalised).Select(m => m.Value).ToHashSet();
        return words.Any(word => word.Contains(' ') ? normalised.Contains(word) : tokens.Contains(word));
    }

    private static double? FirstNumber(string normalised)
    {
        var match = NumberRegex().Match(normalised);
        return match.Success ? ToDouble(match.Value) : null;
    }

    private static double? LastNumber(string normalised)
    {
        var matches = NumberRegex().Matches(normalised);
        return matches.Count > 0 ? ToDouble(matches[^1].Value) : null;
    }

    private static double ToDouble(string raw)
        => double.Parse(raw.Replace(',', '.'), CultureInfo.InvariantCulture);

    private static ArgumentException Ambiguous(string label, string expectation)
        => new($"Could not read the selection out of label \"{label}\": {expectation}.", nameof(MarketOdds.Label));

    [GeneratedRegex(@"[+-]?\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[+-]?[a-z0-9.,]+")]
    private static partial Regex TokenRegex();
}
