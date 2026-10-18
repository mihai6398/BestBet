using System.Text.RegularExpressions;

namespace BestBet.Services;

/// <summary>Where a pasted match link points.</summary>
public enum MatchProvider
{
    /// <summary>FotMob, which answers a plain request and is therefore the one we can poll.</summary>
    FotMob,

    /// <summary>Sofascore. The link is readable; their API is not. See <see cref="MatchReference"/>.</summary>
    Sofascore,
}

/// <summary>
/// What a pasted match link or id resolves to before anything is fetched.
///
/// Two providers, read very differently, and the difference is not cosmetic. A FotMob link carries
/// the id FotMob's own API wants, so the reference is complete the moment it is parsed. A Sofascore
/// link carries a Sofascore id, which is worth nothing to anyone else — and Sofascore answers a
/// programmatic request with 403, down to robots.txt, so there is no way to turn that id into a
/// match server-side. The only thing their URL gives up for free is the team names in the slug, so
/// that is what is kept, and the match is found at FotMob by name instead.
///
/// Parsing is deliberately separate from fetching: a user who pastes the wrong thing finds out from
/// a 400 rather than from a request going out to a provider about a match that does not exist.
/// </summary>
/// <param name="Provider">Which site the input came from.</param>
/// <param name="FotMobId">The FotMob match id, when the input already carried one.</param>
/// <param name="TeamsLabel">
/// Team names as the URL spelled them, for a Sofascore link that has to be matched by name.
/// Kept as one label rather than split in two: a slug joins both names with the same hyphen it
/// uses inside them, so "estrela-amadora-arouca" has no honest split point. The name matching
/// works on a bag of words anyway, which makes the split unnecessary.
/// </param>
public readonly record struct MatchReference(
    MatchProvider Provider,
    string? FotMobId,
    string? TeamsLabel)
{
    /// <summary>
    /// Reads a match id, a FotMob link or a Sofascore link. Null when the input is none of those.
    /// </summary>
    /// <remarks>
    /// Accepts more shapes than it strictly has to, because every one of them is something a user
    /// actually ends up with: the bare id from the address bar, a share link with a tracking query
    /// glued on, and FotMob's own two URL styles — the short <c>/match/5881191</c> and the long
    /// <c>/matches/{slug}/{hash}#5881191</c>, where the id is in the fragment.
    /// </remarks>
    public static MatchReference? Parse(string? input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // A bare id. Up to twelve digits because FotMob's live-only ids run to ten (1000022162),
        // at least five because an ordinary fixture is seven (5881191) and a shorter number is far
        // more likely to be a minute or a score the user pasted into the wrong box.
        if (WholeId.IsMatch(trimmed))
        {
            return new MatchReference(MatchProvider.FotMob, trimmed, null);
        }

        if (!Uri.TryCreate(Absolute(trimmed), UriKind.Absolute, out var url))
        {
            return null;
        }

        var host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? url.Host[4..]
            : url.Host;

        return host.ToLowerInvariant() switch
        {
            "fotmob.com" => ReadFotMob(url),
            "sofascore.com" => ReadSofascore(url),
            _ => null,
        };
    }

    /// <summary>
    /// A FotMob link. The id is either the last path segment or the fragment, depending on which of
    /// their two URL styles the user copied.
    /// </summary>
    private static MatchReference? ReadFotMob(Uri url)
    {
        // "#5881191", and sometimes "#5881191:tab:stats" — the fragment carries more than the id.
        var fragment = LeadingId.Match(url.Fragment.TrimStart('#'));
        if (fragment.Success)
        {
            return new MatchReference(MatchProvider.FotMob, fragment.Groups[1].Value, null);
        }

        // "/match/5881191" or "/match/5881191/eintracht-frankfurt-vs-koln".
        foreach (var segment in Segments(url))
        {
            if (WholeId.IsMatch(segment))
            {
                return new MatchReference(MatchProvider.FotMob, segment, null);
            }
        }

        return null;
    }

    /// <summary>
    /// A Sofascore link, read for the one thing in it we can use anywhere else: the team names.
    /// </summary>
    /// <remarks>
    /// Their id is in the URL too, in the fragment as "#id:12345" or as a numeric segment, and it is
    /// deliberately ignored — it only means something to an API that will not answer us. Taking it
    /// would mean handing FotMob a number from the wrong catalogue, which is how you end up reading
    /// a confident clock off an unrelated match.
    /// </remarks>
    private static MatchReference? ReadSofascore(Uri url)
    {
        // The slug sits right after "match" on the current URLs, and is the first slug-shaped
        // segment on the older ones. Either way it is the segment with a hyphen and no digits.
        var segments = Segments(url);

        for (var i = 0; i < segments.Count; i++)
        {
            if (!string.Equals(segments[i], "match", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < segments.Count && IsTeamSlug(segments[i + 1]))
            {
                return new MatchReference(MatchProvider.Sofascore, null, Deslug(segments[i + 1]));
            }
        }

        var fallback = segments.FirstOrDefault(IsTeamSlug);
        return fallback is null
            ? null
            : new MatchReference(MatchProvider.Sofascore, null, Deslug(fallback));
    }

    /// <summary>
    /// Whether a path segment looks like "arouca-porto" rather than a sport, an id or a URL hash.
    /// </summary>
    private static bool IsTeamSlug(string segment) =>
        segment.Contains('-')
        && segment.Length >= 5
        && !segment.Any(char.IsDigit);

    /// <summary>"estrela-amadora-arouca" back to words. Which words belong to which team is not
    /// decided here — see <see cref="TeamsLabel"/>.</summary>
    private static string Deslug(string segment) => segment.Replace('-', ' ');

    private static List<string> Segments(Uri url) =>
        url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>A link copied without its scheme is still a link, and users copy them that way.</summary>
    private static string Absolute(string input) =>
        input.Contains("://", StringComparison.Ordinal) ? input : $"https://{input}";

    private static readonly Regex WholeId = new(@"^\d{5,12}$", RegexOptions.Compiled);

    private static readonly Regex LeadingId = new(@"^(\d{5,12})(?!\d)", RegexOptions.Compiled);
}
