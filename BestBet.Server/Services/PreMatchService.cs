using System.Globalization;
using System.Text;
using BestBet.Contracts;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Runs the pre-match model end to end: reads the competition's table and both squads, builds each
/// side's expected goals, and projects them onto the markets this app prices.
/// </summary>
public class PreMatchService(ApiFootballClient source, TimeProvider clock)
{
    /// <summary>Competitions offered in the UI, by the data source's own ids.</summary>
    public static readonly IReadOnlyList<LeagueOption> Leagues =
    [
        new(39, "Premier League"),
        new(140, "La Liga"),
        new(135, "Serie A"),
        new(78, "Bundesliga"),
        new(61, "Ligue 1"),
        new(88, "Eredivisie"),
        new(94, "Primeira Liga"),
        new(283, "Liga 1 România"),
        new(2, "UEFA Champions League"),
        new(3, "UEFA Europa League"),
    ];

    /// <summary>Total-goals lines shown in the summary.</summary>
    private static readonly double[] TotalLines = [0.5, 1.5, 2.5, 3.5, 4.5];

    /// <summary>Fewer league games than this and a side's averages are mostly noise.</summary>
    private const int SmallSample = 5;

    /// <summary>Players returned per team: enough to find the scorers, few enough to read.</summary>
    private const int PlayersPerTeam = 12;

    public bool IsConfigured => source.IsConfigured;

    /// <summary>The season a date belongs to, as its start year: August 2025 and March 2026 are both 2025.</summary>
    public static int SeasonFor(DateTimeOffset now) => now.Month >= 7 ? now.Year : now.Year - 1;

    public PreMatchStatus Status() => new(IsConfigured, Leagues, SeasonFor(clock.GetUtcNow()));

    public async Task<PreMatchResponse> AnalyzeAsync(PreMatchRequest request, CancellationToken cancellationToken)
    {
        var league = Leagues.FirstOrDefault(l => l.Id == request.LeagueId)
                     ?? throw new AnalysisInputException($"Unknown competition {request.LeagueId}.");

        if (string.IsNullOrWhiteSpace(request.HomeTeam) || string.IsNullOrWhiteSpace(request.AwayTeam))
        {
            throw new AnalysisInputException("Both team names are required.");
        }

        var season = request.Season ?? SeasonFor(clock.GetUtcNow());
        var warnings = new List<string>();

        Standings table;
        try
        {
            table = await source.GetStandingsAsync(league.Id, season, cancellationToken);
        }
        catch (PreMatchSourceException ex)
        {
            throw new AnalysisInputException(ex.Message);
        }

        if (table.Teams.Count == 0)
        {
            throw new AnalysisInputException(
                $"No table was returned for {league.Name} {season}/{(season + 1) % 100:00}. " +
                "The season may not have started, or the account may not cover it.");
        }

        var home = Find(table, request.HomeTeam);
        var away = Find(table, request.AwayTeam);
        if (home.Id == away.Id)
        {
            throw new AnalysisInputException("The two teams are the same team.");
        }

        var leagueRates = table.League;
        if (leagueRates is null)
        {
            leagueRates = PreMatchModel.FallbackLeague;
            warnings.Add("The competition has too few games for its own averages; typical figures were used.");
        }

        var (baseHome, baseAway) = PreMatchModel.TeamExpectedGoals(home.Form, away.Form, leagueRates);

        var homeSquad = await TrySquadAsync(home, season, warnings, cancellationToken);
        var awaySquad = await TrySquadAsync(away, season, warnings, cancellationToken);

        var homeAdjusted = PreMatchModel.ApplySquad(
            baseHome,
            MatchesPlayed(homeSquad, home.Form),
            homeSquad,
            (request.HomeAbsent ?? []).ToHashSet());

        var awayAdjusted = PreMatchModel.ApplySquad(
            baseAway,
            MatchesPlayed(awaySquad, away.Form),
            awaySquad,
            (request.AwayAbsent ?? []).ToHashSet());

        foreach (var team in new[] { home, away })
        {
            if (team.Form.Played < SmallSample)
            {
                warnings.Add(
                    $"{team.Name} has only {team.Form.Played} games in this competition this season; " +
                    "its averages are mostly the league's.");
            }
        }

        var rates = new PreMatchRates(homeAdjusted.ExpectedGoals, awayAdjusted.ExpectedGoals);
        var distribution = PoissonGoalModel.FullMatch(rates.HomeExpectedGoals, rates.AwayExpectedGoals);

        return new PreMatchResponse(
            league.Id,
            league.Name,
            season,
            leagueRates,
            ToTeam(home, homeAdjusted),
            ToTeam(away, awayAdjusted),
            rates,
            Summarise(distribution),
            warnings,
            AnalysisService.Disclaimer);
    }

    private async Task<IReadOnlyList<PlayerSeason>> TrySquadAsync(
        StandingsTeam team,
        int season,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        // The squad refines the team numbers; it is not required for them. A quota that ran out on
        // the third request should cost the player layer, not the whole answer.
        try
        {
            var squad = await source.GetSquadAsync(team.Id, season, cancellationToken);
            if (squad.Count == 0)
            {
                warnings.Add($"No player statistics were returned for {team.Name}; only team form was used.");
            }

            return squad;
        }
        catch (PreMatchSourceException ex)
        {
            warnings.Add($"Player statistics for {team.Name} could not be read ({ex.Message}); only team form was used.");
            return [];
        }
    }

    /// <summary>
    /// Matches the club has played in all competitions. The table only counts the league, so the most
    /// appearances any one player has made stands in for it.
    /// </summary>
    private static int MatchesPlayed(IReadOnlyList<PlayerSeason> squad, TeamForm form)
        => Math.Max(form.Played, squad.Count == 0 ? 0 : squad.Max(p => p.Appearances));

    private static PreMatchTeam ToTeam(StandingsTeam team, SquadAdjustment squad) => new(
        team.Id,
        team.Name,
        team.Form,
        squad.ExpectedGoals,
        squad.BaseExpectedGoals,
        squad.MissingShare,
        squad.Players.Take(PlayersPerTeam).ToList());

    private static PreMatchProbabilities Summarise(GoalDistribution distribution)
    {
        double Of(MarketKind kind, MarketSelection side, double? line = null)
            => MarketProbabilityResolver.Resolve(kind, new MarketSelectionSpec(side, line), distribution).RawWinProbability;

        return new PreMatchProbabilities(
            Of(MarketKind.Moneyline1X2, MarketSelection.Home),
            Of(MarketKind.Moneyline1X2, MarketSelection.Draw),
            Of(MarketKind.Moneyline1X2, MarketSelection.Away),
            Of(MarketKind.BothTeamsToScore, MarketSelection.Yes),
            TotalLines.Select(line => new TotalLine(line, Of(MarketKind.TotalGoals, MarketSelection.Over, line))).ToList());
    }

    /// <summary>
    /// Finds a team by the name the user typed. Bookmakers and the data source disagree about names
    /// ("Man United", "Manchester United", "Bayern München"), so this is deliberately forgiving — but
    /// not so forgiving that it picks one of two candidates at random.
    /// </summary>
    public static StandingsTeam Find(Standings table, string typed)
    {
        var wanted = Normalise(typed);
        if (wanted.Length == 0)
        {
            throw new AnalysisInputException("A team name is required.");
        }

        var named = table.Teams.Select(t => (Team: t, Name: Normalise(t.Name))).ToList();

        var exact = named.Where(x => x.Name == wanted).ToList();
        if (exact.Count == 1)
        {
            return exact[0].Team;
        }

        var contains = named.Where(x => x.Name.Contains(wanted) || wanted.Contains(x.Name)).ToList();
        if (contains.Count == 1)
        {
            return contains[0].Team;
        }

        // Word overlap catches "Man United" against "Manchester United" and "Inter Milan" against "Inter".
        var words = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = named
            .Select(x => (x.Team, Score: words.Count(w => x.Name.Split(' ').Any(n => n.StartsWith(w) || w.StartsWith(n)))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count > 0 && (scored.Count == 1 || scored[0].Score > scored[1].Score))
        {
            return scored[0].Team;
        }

        var candidates = (contains.Count > 1 ? contains.Select(x => x.Team) : table.Teams)
            .Select(t => t.Name)
            .Take(30);

        throw new AnalysisInputException(
            (contains.Count > 1
                ? $"\"{typed}\" matches more than one team: "
                : $"\"{typed}\" is not a team in this competition. The teams are: ") +
            string.Join(", ", candidates) + ".");
    }

    /// <summary>Lower-case, accents removed, club suffixes dropped.</summary>
    public static string Normalise(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
        }

        var drop = new HashSet<string> { "fc", "cf", "afc", "sc", "ac", "as", "ss", "us", "club", "de", "the" };
        return string.Join(' ', plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !drop.Contains(w)));
    }
}
