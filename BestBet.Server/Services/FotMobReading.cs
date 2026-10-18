using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BestBet.Contracts;

namespace BestBet.Services;

/// <summary>
/// Turns one FotMob <c>matchDetails</c> document into the reading the rest of the app consumes.
///
/// Kept apart from <see cref="FotMobClient"/> and given nothing but a JSON document, because this
/// is the half that can be wrong in a way no exception reports: a stat read off the wrong row, or a
/// clock rounded the other way, comes out looking like a perfectly good number and goes on to size
/// a stake. So it is pure, and the tests run it against responses captured from the real API —
/// a live match, a league with no xG, a match that has not kicked off.
///
/// It reads the document loosely on purpose. FotMob owes this app nothing: fields appear and vanish
/// by league and by how far along a match is, and a strict binding would turn one absent stat into
/// a failure to fetch anything at all. So everything optional is probed and skipped, while the
/// fields an analysis genuinely needs are reported as missing rather than defaulted — see
/// <see cref="ProviderReading.Missing"/>.
/// </summary>
public static class FotMobReading
{
    /// <summary>The fields an analysis cannot run without. Mirrors REQUIRED in <c>statsPaste.ts</c>.</summary>
    private static readonly string[] Required = ["minute", "xgHome", "xgAway"];

    public static ProviderReading From(JsonDocument document, string matchId)
    {
        var root = document.RootElement;
        var general = Child(root, "general");
        var status = Child(Child(root, "header"), "status");

        var id = Text(general, "matchId") ?? matchId;
        var xg = Stat(root, "expected_goals");
        var score = Score(Text(status, "scoreStr"));

        var reading = new ProviderReading(
            FotMobClient.DisplayName,
            id,
            $"https://www.fotmob.com/match/{id}",
            Label(general),
            Minute(status),
            score?.Home,
            score?.Away,
            Number(xg?.Home),
            Number(xg?.Away),
            Extras(root),
            [],
            Flag(root, "ongoing") ?? Flag(status, "ongoing") ?? false,
            Flag(status, "finished") ?? Flag(general, "finished") ?? false,
            DateTime.UtcNow);

        return reading with { Missing = Required.Where(field => IsMissing(reading, field)).ToList() };
    }

    private static bool IsMissing(ProviderReading reading, string field) => field switch
    {
        "minute" => reading.Minute is null,
        "xgHome" => reading.XgHome is null,
        "xgAway" => reading.XgAway is null,
        _ => false,
    };

    /// <summary>"Club Bolivar - Olimpia", in the same shape the paste parser produces.</summary>
    /// <remarks>
    /// Built from the two team objects rather than from <c>general.matchName</c>, which is a slug
    /// with the kickoff time glued on ("Club Bolivar-vs-Olimpia_Fri, Oct 16, 2026, 20:00 UTC") and
    /// would not survive the name matching that decides which saved bets a reading may re-price.
    /// </remarks>
    private static string? Label(JsonElement general)
    {
        var home = Text(Child(general, "homeTeam"), "name");
        var away = Text(Child(general, "awayTeam"), "name");

        return home is null || away is null ? null : $"{home} - {away}";
    }

    /// <summary>
    /// The minute in progress.
    /// </summary>
    /// <remarks>
    /// The running clock is read first and rounded the same way the paste parser rounds it: "62:08"
    /// is the 63rd minute, because any seconds at all mean that minute is being played. That rule is
    /// copied rather than reinvented so a fetched reading and a pasted one never disagree about what
    /// minute it is — two conventions would show up as a bet re-priced against a clock one minute
    /// off, which is small, silent and wrong.
    ///
    /// The fallbacks exist because <c>liveTime.long</c> is not always a clock. At the interval it is
    /// the word "Half-Time", and once the match ends there is no <c>liveTime</c> at all, only
    /// <c>reason</c> — so the same reader is pointed at each of those in turn.
    /// </remarks>
    private static int? Minute(JsonElement status)
    {
        var live = Child(status, "liveTime");
        var reason = Child(status, "reason");

        string?[] candidates =
        [
            Text(live, "long"),
            Text(live, "short"),
            Text(reason, "short"),
            Text(reason, "long"),
        ];

        return candidates.Select(MinuteIn).FirstOrDefault(minute => minute is not null);
    }

    /// <summary>
    /// Reads a minute out of one of FotMob's time strings. Mirrors <c>minuteOn</c> in <c>statsPaste.ts</c>.
    /// </summary>
    private static int? MinuteIn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // FotMob wraps the prime in left-to-right marks ("63‎’‎"), which are invisible on
        // screen and break every pattern they appear in. Dropped before anything else is tried.
        var text = Invisible.Replace(raw, string.Empty).Trim();

        // "62:08" — a running clock, so the 63rd minute is the one in progress.
        var clock = Clock.Match(text);
        if (clock.Success)
        {
            var minutes = Digits(clock.Groups[1]);
            return Clamp(Digits(clock.Groups[2]) > 0 ? minutes + 1 : minutes);
        }

        // "45+2", with or without the prime.
        var stoppage = Stoppage.Match(text);
        if (stoppage.Success)
        {
            return Clamp(Digits(stoppage.Groups[1]) + Digits(stoppage.Groups[2]));
        }

        // "63’"
        var prime = Prime.Match(text);
        if (prime.Success)
        {
            return Clamp(Digits(prime.Groups[1]));
        }

        // The interval and the end, however FotMob happens to word them.
        if (HalfTime.IsMatch(text)) return 45;
        if (FullTime.IsMatch(text)) return 90;

        return null;
    }

    /// <summary>Beyond this it is not a minute of football, so it is treated as not having been read.</summary>
    private static int? Clamp(int minute) => minute is >= 1 and <= 130 ? minute : null;

    /// <summary>"1 - 2". Null before kickoff, when FotMob sends no scoreline at all.</summary>
    private static (int Home, int Away)? Score(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var match = ScoreLine.Match(raw);

        return match.Success ? (Digits(match.Groups[1]), Digits(match.Groups[2])) : null;
    }

    /// <summary>
    /// The extra stats, under the keys the client already knows how to name in the user's language.
    /// </summary>
    /// <remarks>
    /// The first column is FotMob's key, the second is the stat id in <c>statsPaste.ts</c>, the
    /// third its English name — the same fallback label the paste parser attaches. Only the stats
    /// that parser recognises are carried across: anything else would arrive as a row the interface
    /// has no name for, and a panel full of provider keys is worse than a shorter panel.
    /// </remarks>
    private static readonly (string Key, string Id, string Label)[] Recognised =
    [
        ("ShotsOnTarget", "shotsOnTarget", "Shots on target"),
        ("big_chance", "bigChances", "Big chances"),
        ("total_shots", "shots", "Total shots"),
        ("BallPossesion", "possession", "Possession"),
        ("corners", "corners", "Corners"),
        ("keeper_saves", "saves", "Goalkeeper saves"),
        ("fouls", "fouls", "Fouls"),
        ("yellow_cards", "yellowCards", "Yellow cards"),
    ];

    private static List<ProviderStatRow> Extras(JsonElement root)
    {
        var rows = new List<ProviderStatRow>();

        foreach (var (key, id, label) in Recognised)
        {
            if (Stat(root, key) is { } pair)
            {
                rows.Add(new ProviderStatRow(id, label, pair.Home, pair.Away));
            }
        }

        return rows;
    }

    /// <summary>
    /// Finds one stat's home and away values, as the provider printed them.
    /// </summary>
    /// <remarks>
    /// Walks every group under <c>Periods.All</c> rather than looking in "Top stats", because which
    /// group a stat lives in moves around — xG sits in both the top block and its own, and in a
    /// league without xG neither of those exists.
    ///
    /// The null check is the part that matters. Each group opens with a heading row carrying the
    /// SAME key as the real stat and a pair of nulls for its values. Taken at face value that would
    /// claim the stat is present and empty, which reads as "this match has no xG" for a match that
    /// has it two rows further down.
    /// </remarks>
    private static (string Home, string Away)? Stat(JsonElement root, string key)
    {
        var all = Child(Child(Child(Child(root, "content"), "stats"), "Periods"), "All");

        foreach (var group in Items(all, "stats"))
        {
            foreach (var stat in Items(group, "stats"))
            {
                if (!string.Equals(Text(stat, "key"), key, StringComparison.Ordinal))
                {
                    continue;
                }

                var values = Child(stat, "stats");

                if (values.ValueKind == JsonValueKind.Array
                    && values.GetArrayLength() >= 2
                    && Printed(values[0]) is { } home
                    && Printed(values[1]) is { } away)
                {
                    return (home, away);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A stat value as text. Null for the headings and for anything that is not a value.
    /// </summary>
    /// <remarks>
    /// Mixed types are normal here: possession comes back as the number 56, xG as the string "1.34".
    /// Both are kept as the provider printed them and only the fields the model needs are turned
    /// into numbers — the rest exist to be looked at.
    /// </remarks>
    private static string? Printed(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    /// <summary>A number the model will use, or null when the provider printed something else.</summary>
    private static double? Number(string? raw) =>
        raw is not null
        && double.TryParse(raw.Replace("%", string.Empty).Trim(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int Digits(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    private static readonly Regex Invisible = new(@"[\p{Cc}\p{Cf}]", RegexOptions.Compiled);
    private static readonly Regex Clock = new(@"^(\d{1,3}):([0-5]\d)$", RegexOptions.Compiled);
    private static readonly Regex Stoppage = new(@"\b(\d{1,3})\s*\+\s*(\d{1,2})", RegexOptions.Compiled);
    private static readonly Regex Prime = new(@"\b(\d{1,3})\s*[’']", RegexOptions.Compiled);
    private static readonly Regex ScoreLine = new(@"^\s*(\d{1,2})\s*-\s*(\d{1,2})\s*$", RegexOptions.Compiled);
    private static readonly Regex HalfTime = new(@"^(ht|half[- ]?time)$", Ignoring);
    private static readonly Regex FullTime = new(@"^(ft|full[- ]?time)$", Ignoring);

    private const RegexOptions Ignoring = RegexOptions.Compiled | RegexOptions.IgnoreCase;

    // --- Loose reads over a document that owes us nothing --------------------

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child)
            ? child
            : default;

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name)
    {
        var array = Child(parent, name);
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
    }

    private static string? Text(JsonElement parent, string name)
    {
        var value = Child(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool? Flag(JsonElement parent, string name) => Child(parent, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
