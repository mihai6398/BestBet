namespace BestBet.Domain;

/// <summary>
/// How a team has scored and conceded this season, per game, split by where it played. The split
/// matters: a side that scores 2.4 at home and 1.0 away is two different attacks.
/// </summary>
public record TeamForm(
    int HomePlayed,
    int AwayPlayed,
    double GoalsForHome,
    double GoalsAgainstHome,
    double GoalsForAway,
    double GoalsAgainstAway)
{
    public int Played => HomePlayed + AwayPlayed;
}

/// <summary>Average goals per match in the competition, home side and away side separately.</summary>
public record LeagueRates(double HomeGoals, double AwayGoals);

/// <summary>
/// One player's season so far, summed over every competition the club played him in.
/// </summary>
/// <param name="Position">"Attacker", "Midfielder", "Defender", "Goalkeeper", as the data source names it.</param>
/// <param name="Appearances">Matches he played any part of.</param>
/// <param name="Minutes">Minutes on the pitch.</param>
/// <param name="Goals">Goals scored, penalties included.</param>
/// <param name="Shots">Total shots, on or off target.</param>
public record PlayerSeason(
    int Id,
    string Name,
    string? Position,
    int Appearances,
    int Minutes,
    int Goals,
    int Shots);

/// <summary>One player's chance of scoring in this match.</summary>
/// <param name="ScoringProbability">P(at least one goal), 0..1.</param>
/// <param name="FairOdds">1 / ScoringProbability: the price at which backing him breaks even.</param>
/// <param name="GoalsPer90">Season rate after shrinking towards what his position usually produces.</param>
/// <param name="ExpectedMatchFraction">How much of a match he is expected to be on the pitch.</param>
/// <param name="Absent">Marked as not playing; his chance is then zero.</param>
public record PlayerScoringChance(
    int Id,
    string Name,
    string? Position,
    int Appearances,
    int Minutes,
    int Goals,
    int Shots,
    double GoalsPer90,
    double ExpectedMatchFraction,
    double ScoringProbability,
    double FairOdds,
    bool Absent);

/// <summary>What the squad layer did to a team's scoring rate.</summary>
/// <param name="ExpectedGoals">The team's goals in this match, after absences.</param>
/// <param name="BaseExpectedGoals">The same, before absences — the team-form number alone.</param>
/// <param name="MissingShare">Share (0..1) of the squad's expected scoring that is absent.</param>
public record SquadAdjustment(
    double ExpectedGoals,
    double BaseExpectedGoals,
    double MissingShare,
    IReadOnlyList<PlayerScoringChance> Players);
