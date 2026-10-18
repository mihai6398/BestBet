using BestBet.Domain;

namespace BestBet.Contracts;

/// <summary>A competition the pre-match model can read, as the data source numbers it.</summary>
public record LeagueOption(int Id, string Name);

/// <summary>Response of <c>GET /api/prematch/status</c>: can it run at all, and over what.</summary>
/// <param name="Configured">Whether an API key has been set. Without one the rest of the app is unaffected.</param>
public record PreMatchStatus(bool Configured, IReadOnlyList<LeagueOption> Leagues, int CurrentSeason);

/// <summary>
/// Body of <c>POST /api/prematch</c>.
/// </summary>
/// <param name="LeagueId">The competition both teams' season is read from.</param>
/// <param name="HomeTeam">Typed name; matched loosely against the competition's own team list.</param>
/// <param name="AwayTeam">Same.</param>
/// <param name="Season">Start year of the season (2025 for 2025/26). Omit for the current one.</param>
/// <param name="HomeAbsent">Player ids who will not play for the home side.</param>
/// <param name="AwayAbsent">Player ids who will not play for the away side.</param>
public record PreMatchRequest(
    int LeagueId,
    string HomeTeam,
    string AwayTeam,
    int? Season = null,
    int[]? HomeAbsent = null,
    int[]? AwayAbsent = null);

/// <summary>One team's side of the pre-match picture.</summary>
/// <param name="Form">The season averages the expected goals were built from.</param>
/// <param name="ExpectedGoals">After absences. This is the number the probabilities use.</param>
/// <param name="BaseExpectedGoals">Before absences, from team form and the opponent's defence alone.</param>
/// <param name="MissingShare">Share (0..1) of the squad's expected scoring that is marked absent.</param>
/// <param name="Players">The squad's likeliest scorers, best first. Empty when squad data could not be read.</param>
public record PreMatchTeam(
    int Id,
    string Name,
    TeamForm Form,
    double ExpectedGoals,
    double BaseExpectedGoals,
    double MissingShare,
    IReadOnlyList<PlayerScoringChance> Players);

/// <summary>P(total goals above the line), for one line.</summary>
public record TotalLine(double Line, double Over);

/// <summary>What the expected goals imply for the markets this app prices.</summary>
public record PreMatchProbabilities(
    double HomeWin,
    double Draw,
    double AwayWin,
    double BothTeamsToScore,
    IReadOnlyList<TotalLine> Totals);

/// <summary>Response of <c>POST /api/prematch</c>.</summary>
/// <param name="Rates">Feed this to <c>POST /api/analyze</c> as <c>preMatch</c> to price any market with it.</param>
/// <param name="Warnings">Things that make the numbers less trustworthy, in plain words.</param>
public record PreMatchResponse(
    int LeagueId,
    string LeagueName,
    int Season,
    LeagueRates League,
    PreMatchTeam Home,
    PreMatchTeam Away,
    PreMatchRates Rates,
    PreMatchProbabilities Probabilities,
    IReadOnlyList<string> Warnings,
    string Disclaimer);
