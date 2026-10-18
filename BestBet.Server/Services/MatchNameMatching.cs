using System.Globalization;
using System.Text;

namespace BestBet.Services;

/// <summary>
/// Whether two labels name the same match, decided on team names alone.
///
/// This is the C# half of the rule in <c>bestbet.client/src/lib/liveFeed.ts</c>, which decides
/// which saved bets a reading may re-price. The same question comes up here for a different reason:
/// a Sofascore link gives up nothing but the team names in its slug, so finding that match at
/// FotMob means comparing names and nothing else.
///
/// Both halves have to stay in step — a name the client considers the same match and the server
/// does not would show a reading for a match whose bets never get re-priced. So the rule, the word
/// filter and the noise list below are a deliberate mirror of the TypeScript, not a second attempt
/// at the problem: change one and change the other, and the tests on both sides cover the same
/// cases so a drift fails rather than ships.
///
/// It errs towards saying no. Two labels have to share two real words, unless one of them only has
/// one word to give — because the cost of a false yes is pricing one match's bet against another
/// match's clock, which produces a confident number about a game it has nothing to do with.
/// </summary>
public static class MatchNameMatching
{
    public static bool SameMatch(string? a, string? b)
    {
        var left = SignificantWords(a);
        var right = SignificantWords(b);

        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        return SharedWords(left, right) >= Math.Min(2, Math.Min(left.Count, right.Count));
    }

    /// <summary>
    /// How many real words two labels have in common.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SameMatch"/> because picking a match out of a whole day's fixtures
    /// needs more than a yes: several of them can pass the gate on a common word, and the one that
    /// shares the most names is the one to read. The gate still decides who is eligible.
    /// </remarks>
    public static int SharedWords(string? a, string? b) =>
        SharedWords(SignificantWords(a), SignificantWords(b));

    private static int SharedWords(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right) =>
        left.Count(right.Contains);

    /// <summary>Club names, with the noise a header or a URL slug carries around them dropped.</summary>
    public static List<string> SignificantWords(string? label)
    {
        var folded = Fold(label ?? string.Empty);
        var words = new List<string>();

        foreach (var word in folded.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 3 && !Noise.Contains(word) && !words.Contains(word))
            {
                words.Add(word);
            }
        }

        return words;
    }

    /// <summary>
    /// Lower-cased, stripped of diacritics, and with everything that is not a letter or digit turned
    /// into a separator — so "Atlético Madrid – Betis", "atletico-madrid-betis" and
    /// "Atletico Madrid - Betis" all come apart into the same words.
    /// </summary>
    private static string Fold(string label)
    {
        var decomposed = label.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : ' ');
        }

        return builder.ToString();
    }

    private static readonly char[] Separators = [' '];

    /// <summary>
    /// Words that appear in half the club names in Europe and tell two matches apart in none of them.
    /// Mirrors NOISE in <c>liveFeed.ts</c>.
    /// </summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "fc",
        "cf",
        "afc",
        "cfr",
        "sport",
        "sporting",
        "club",
        "city",
        "town",
        "united",
        "real",
        "athletic",
        "atletico",
        "deportivo",
        "the",
        "vs",
    };
}
