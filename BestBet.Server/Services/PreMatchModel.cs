using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Pre-match goal expectation from how the two teams have scored and conceded, and from who is
/// actually going to be on the pitch.
/// </summary>
/// <remarks>
/// Before this, a pre-match analysis had no model at all: the probability was whatever the user typed.
/// This is the standard attack-strength / defence-strength construction, with two things added that
/// matter for the kind of match this app is used on:
///
///   1. SHRINKAGE. Four games into a season a side's average is mostly luck. Every per-game average
///      is blended with the league average, weighted by <see cref="PriorGames"/> games' worth of
///      prior, so early-season numbers move slowly and a long sample is trusted.
///
///   2. THE SQUAD. A team's season average describes the team that played those games. If its top
///      scorer is out, that is not the team about to play. Each player's scoring rate is measured from
///      goals and shots (shots are far more stable than goals), shrunk towards what his position
///      usually produces, and weighted by how much of a match he is likely to play. A player marked
///      absent takes his share of the goals with him, less the part the replacement keeps.
///
/// What it does not do: it knows nothing about this fixture's referee, weather, motivation, or
/// tactics, and treats the two scores as independent. Read the output as a better-informed prior, not
/// as a price — which is why it feeds the same edge test as everything else rather than bypassing it.
/// </remarks>
public static class PreMatchModel
{
    /// <summary>How many games of league-average form each team average is blended with.</summary>
    public const double PriorGames = 6.0;

    /// <summary>Used when the competition's own averages could not be read.</summary>
    public static readonly LeagueRates FallbackLeague = new(1.50, 1.20);

    /// <summary>Hard bounds on a team's expected goals; outside these the inputs are broken, not extreme.</summary>
    public const double MinExpectedGoals = 0.25;
    public const double MaxExpectedGoals = 4.0;

    /// <summary>
    /// Share of an absent player's scoring that is lost outright. The rest is picked up by whoever
    /// takes his place and by the team's other players: a replacement is worse, not nothing.
    /// </summary>
    public const double AbsenceLossFraction = 0.70;

    /// <summary>Shots are worth this many goals each, league-wide. Goals scored as a share of shots.</summary>
    public const double ShotConversion = 0.10;

    /// <summary>How much of a player's evidence comes from goals rather than shots.</summary>
    public const double GoalWeight = 0.60;

    /// <summary>The prior rate is worth this many full matches of the player's own minutes.</summary>
    public const double PlayerPriorMatches = 6.0;

    /// <summary>Goals per 90 an average player in that position produces, in a major league.</summary>
    public static double PositionPriorPer90(string? position) => position?.ToLowerInvariant() switch
    {
        "attacker" => 0.30,
        "midfielder" => 0.09,
        "defender" => 0.03,
        "goalkeeper" => 0.0,
        _ => 0.09,
    };

    /// <summary>
    /// Expected goals for each side, from team form alone: league rate times attack strength times the
    /// opponent's defence strength.
    /// </summary>
    public static (double Home, double Away) TeamExpectedGoals(TeamForm home, TeamForm away, LeagueRates league)
    {
        var mu = league.HomeGoals;
        var nu = league.AwayGoals;

        // Home attack against away defence, both measured in the home-scoring currency.
        var homeFor = Shrink(home.GoalsForHome, home.HomePlayed, mu);
        var awayAgainst = Shrink(away.GoalsAgainstAway, away.AwayPlayed, mu);
        var lambdaHome = mu * (homeFor / mu) * (awayAgainst / mu);

        var awayFor = Shrink(away.GoalsForAway, away.AwayPlayed, nu);
        var homeAgainst = Shrink(home.GoalsAgainstHome, home.HomePlayed, nu);
        var lambdaAway = nu * (awayFor / nu) * (homeAgainst / nu);

        return (Clamp(lambdaHome), Clamp(lambdaAway));
    }

    /// <summary>
    /// Applies the squad to one team's expected goals and works out each player's chance of scoring.
    /// </summary>
    /// <param name="teamExpectedGoals">From <see cref="TeamExpectedGoals"/>.</param>
    /// <param name="teamMatchesPlayed">Matches the club has played this season, across competitions.</param>
    /// <param name="squad">The club's players with their season so far.</param>
    /// <param name="absentIds">Players who will not play.</param>
    public static SquadAdjustment ApplySquad(
        double teamExpectedGoals,
        int teamMatchesPlayed,
        IReadOnlyList<PlayerSeason> squad,
        IReadOnlySet<int> absentIds)
    {
        var scored = squad
            .Select(p => (Player: p, Rate: GoalsPer90(p), Fraction: MatchFraction(p, teamMatchesPlayed)))
            .Select(x => (x.Player, x.Rate, x.Fraction, Weight: x.Rate * x.Fraction))
            .ToList();

        var total = scored.Sum(x => x.Weight);
        var available = scored.Where(x => !absentIds.Contains(x.Player.Id)).Sum(x => x.Weight);

        // With nobody known to score there is nothing to attribute, and nothing to take away.
        var missingShare = total > 0 ? Math.Clamp(1.0 - available / total, 0.0, 1.0) : 0.0;
        var factor = 1.0 - missingShare * AbsenceLossFraction;
        var adjusted = Clamp(teamExpectedGoals * factor);

        // An available player keeps exactly his share of the team's UNADJUSTED goals: what an absence
        // costs the team is the part nobody else picks up, not a haircut on the men who are playing.
        var players = scored
            .OrderByDescending(x => x.Weight)
            .Select(x =>
            {
                var absent = absentIds.Contains(x.Player.Id);
                var expected = total > 0 && !absent ? teamExpectedGoals * x.Weight / total : 0.0;
                var probability = 1.0 - Math.Exp(-expected);

                return new PlayerScoringChance(
                    x.Player.Id,
                    x.Player.Name,
                    x.Player.Position,
                    x.Player.Appearances,
                    x.Player.Minutes,
                    x.Player.Goals,
                    x.Player.Shots,
                    x.Rate,
                    x.Fraction,
                    probability,
                    probability > 0 ? 1.0 / probability : 0.0,
                    absent);
            })
            .ToList();

        return new SquadAdjustment(adjusted, teamExpectedGoals, missingShare, players);
    }

    /// <summary>
    /// Goals per 90, from a blend of goals and shots, shrunk towards the position's usual rate.
    /// </summary>
    public static double GoalsPer90(PlayerSeason player)
    {
        var matches = player.Minutes / 90.0;
        var evidence = GoalWeight * player.Goals + (1.0 - GoalWeight) * player.Shots * ShotConversion;
        var prior = PositionPriorPer90(player.Position);

        return (evidence + prior * PlayerPriorMatches) / (matches + PlayerPriorMatches);
    }

    /// <summary>
    /// Expected share of a match he plays: how often he is in the squad, times how much of it he lasts.
    /// A star who plays every minute is 1.0; a rotation player on 40% of games for 60 minutes is 0.27.
    /// </summary>
    public static double MatchFraction(PlayerSeason player, int teamMatchesPlayed)
    {
        if (player.Appearances <= 0 || teamMatchesPlayed <= 0)
        {
            return 0.0;
        }

        var availability = Math.Min(1.0, player.Appearances / (double)teamMatchesPlayed);
        var perAppearance = Math.Min(90.0, player.Minutes / (double)player.Appearances) / 90.0;
        return availability * perAppearance;
    }

    /// <summary>Blends an observed per-game average with the league average.</summary>
    private static double Shrink(double observed, int games, double prior)
        => (observed * games + prior * PriorGames) / (games + PriorGames);

    private static double Clamp(double value) => Math.Clamp(value, MinExpectedGoals, MaxExpectedGoals);
}
