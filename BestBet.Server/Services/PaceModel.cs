using System.Globalization;
using BestBet.Contracts;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// Turns the xG on the clock into a rate the rest of the match can be priced off.
/// </summary>
/// <remarks>
/// The spec's model divides cumulative xG by minutes played and runs that flat to full time. Tested
/// against a real match — Barcelona 3-1 up at minute 36 on 2.38 xG — that projects 5.95 xG over
/// ninety minutes and prices Over 6.5 at 76%, against a market at 50%. The market was right.
///
/// Three corrections, each switchable, each visible in the output:
///
///   1. SHRINKAGE. A rate read off 36 minutes is a small-sample estimate, not a fact. Blending the
///      observed xG with a prior worth <see cref="PriorStrengthMinutes"/> minutes of play is the
///      standard fix, and it does most of the work: on the match above it alone takes Over 6.5 from
///      76% to 50%. It cuts both ways — a side on 0.30 xG gets pulled UP, because a quiet half hour
///      is not proof of a quiet match.
///
///      The prior is each team's own pre-match expectation when the caller has one, and the league
///      average only when it does not. A single league-wide number treats every side as mid-table:
///      it pulls a top attack on a genuine 2.8-goal pace down towards 1.35, which is a systematic
///      lean towards the Under on exactly the matches where the Over is most often right. The
///      pre-match model already knows who these two teams are, so that is what the evidence is
///      blended with.
///
///   2. RECENCY. When the caller can supply xG for a recent window — Sofascore and friends publish
///      per-half splits — the flat average is blended with that window's rate. This is what catches
///      a game that has just opened up, or one that has died.
///
///   3. SCORE STATE. A side protecting a lead attacks less and a side chasing attacks more. The
///      effect is small early and large late, so it is scaled by how much of the match has gone.
///
///   4. RED CARDS. A side down to ten scores less and concedes more for as long as the match has
///      left — but the xG already on the clock since the card was produced that way too, so only
///      the part of the rate built from eleven-a-side football is corrected.
///
/// It also settles how long the rest of the match is, because a rate is per minute and the two
/// have to be counted in the same minutes: see <see cref="MatchClock"/> for why stoppage time is
/// not a detail.
///
/// The coefficients below are calibrated heuristics, not fitted parameters. They are stated as
/// constants with their reasoning so they can be argued with, and the whole thing can be turned off.
/// </remarks>
public static class PaceModel
{
    /// <summary>
    /// Goals a typical team scores in a match in a major league. The prior every observed rate is
    /// pulled towards.
    /// </summary>
    public const double LeagueAverageGoalsPerTeam = 1.35;

    /// <summary>
    /// How much the prior is worth, in minutes of observed play. At 45 the prior and the evidence
    /// weigh equally at half time: before that the prior dominates, after it the match does. That is
    /// the right shape — ten minutes of football tells you very little, eighty tells you a lot.
    /// </summary>
    public const double PriorStrengthMinutes = 45.0;

    /// <summary>
    /// How much one goal of lead moves each side's attacking rate, at full time. Ten percent per goal
    /// is at the conservative end of what the shot-volume literature reports for game state.
    /// </summary>
    public const double ScoreStateEffectPerGoal = 0.10;

    /// <summary>Leads beyond this stop changing behaviour — 5-0 and 3-0 are both just "over".</summary>
    public const int MaxLeadConsidered = 3;

    /// <summary>
    /// A full-match xG projection above this is a sign the flat extrapolation has left reality: very
    /// few matches produce four expected goals for one side.
    /// </summary>
    public const double ImplausibleProjectedXg = 4.0;

    /// <summary>
    /// What a sending-off does to the side that is down to ten, as a multiple of its scoring rate.
    /// Studies of goal timing around red cards put it near two thirds; 0.70 is the cautious end.
    /// </summary>
    public const double ShortHandedRateFactor = 0.70;

    /// <summary>The same for the side playing against ten: about a quarter more.</summary>
    public const double AgainstShortHandedRateFactor = 1.25;

    /// <summary>Beyond two men the numbers have no data behind them, and the match is usually over.</summary>
    public const int MaxManAdvantageConsidered = 2;

    /// <summary>Seven players is the minimum a side can field, so four red cards is the most there can be.</summary>
    private const int MaxRedCards = 4;

    /// <summary>A board beyond this is not one anybody has held up.</summary>
    private const int MaxAddedTime = 30;

    /// <param name="state">The match as it is now.</param>
    /// <param name="settings">Which corrections to apply.</param>
    /// <param name="prior">Each side's expected goals for the whole match from the pre-match model, used
    /// as the shrinkage target instead of the league average. Null falls back to the league average.</param>
    public static PaceAdjustment Adjust(LiveMatchState state, PaceSettings settings, PreMatchRates? prior = null)
    {
        if (state.IsPreMatch)
        {
            throw new InvalidOperationException(
                "There is no live xG to adjust before kick-off. Supply a manual probability instead.");
        }

        if (state.MinutePlayed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state.MinutePlayed,
                "Minute played must be at least 1; an xG rate cannot be read off zero elapsed minutes.");
        }

        if (state.CumulativeXgHome < 0 || state.CumulativeXgAway < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Cumulative xG cannot be negative.");
        }

        if (state.AddedTimeFirstHalf is < 0 or > MaxAddedTime || state.AddedTimeSecondHalf is < 0 or > MaxAddedTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state), $"Added time is the number on the fourth official's board, 0 to {MaxAddedTime} minutes.");
        }

        if (state.RedCardsHome is < 0 or > MaxRedCards || state.RedCardsAway is < 0 or > MaxRedCards)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                $"Red cards are counted per side, 0 to {MaxRedCards}: a side cannot play on with fewer than seven.");
        }

        if (state.LastRedCardMinute is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "The minute of a red card cannot be negative.");
        }

        if (prior is not null && !(IsPositiveFinite(prior.HomeExpectedGoals) && IsPositiveFinite(prior.AwayExpectedGoals)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(prior),
                "The pre-match expected goals must be positive numbers for both teams.");
        }

        // Minutes of play actually elapsed when stoppage is counted, so the rate and the time it is run
        // over are measured in the same minutes. The spec's clock otherwise.
        var elapsed = settings.Stoppage ? MatchClock.ElapsedMinutes(state) : state.MinutePlayed;
        var matchMinutes = settings.Stoppage ? MatchClock.TypicalMatchMinutes : PoissonGoalModel.RegulationMinutes;
        var remaining = settings.Stoppage
            ? MatchClock.RemainingMinutes(state)
            : PoissonGoalModel.RemainingMinutes(state.MinutePlayed);

        var rawHome = state.CumulativeXgHome / elapsed;
        var rawAway = state.CumulativeXgAway / elapsed;

        var home = rawHome;
        var away = rawAway;
        var steps = new List<string>();
        double? priorHome = null;
        double? priorAway = null;

        if (settings.HasRecentWindow)
        {
            (home, away) = ApplyRecency(state, settings, elapsed, home, away, steps);
        }

        if (settings.Shrink)
        {
            priorHome = prior?.HomeExpectedGoals ?? LeagueAverageGoalsPerTeam;
            priorAway = prior?.AwayExpectedGoals ?? LeagueAverageGoalsPerTeam;
            (home, away) = ApplyShrinkage(
                elapsed, matchMinutes, home, away, priorHome.Value, priorAway.Value, prior is not null, steps);
        }

        if (settings.ScoreState)
        {
            (home, away) = ApplyScoreState(state, home, away, steps);
        }

        if (settings.RedCards)
        {
            (home, away) = ApplyRedCards(state, settings.Shrink, home, away, steps);
        }

        if (settings.Stoppage)
        {
            steps.Add(MatchClock.Describe(state));
        }

        var projectedHome = rawHome * PoissonGoalModel.RegulationMinutes;
        var projectedAway = rawAway * PoissonGoalModel.RegulationMinutes;

        return new PaceAdjustment(
            rawHome,
            rawAway,
            home,
            away,
            projectedHome,
            projectedAway,
            steps.Count > 0,
            steps,
            BuildWarning(projectedHome, projectedAway, state.MinutePlayed, steps.Count > 0),
            elapsed,
            remaining,
            priorHome,
            priorAway,
            settings.Shrink && prior is not null);
    }

    /// <summary>
    /// Blends the whole-match average with the rate inside a recent window. A half-time split is the
    /// usual source: "all match" minus "first half" is the second half.
    /// </summary>
    private static (double Home, double Away) ApplyRecency(
        LiveMatchState state,
        PaceSettings settings,
        double elapsed,
        double home,
        double away,
        List<string> steps)
    {
        var window = settings.RecentWindowMinutes!.Value;

        if (window > elapsed)
        {
            throw new ArgumentException(
                $"The recent window ({Num(window)} min) cannot be longer than the match has been played " +
                $"({Num(elapsed)} min).",
                nameof(settings));
        }

        if (settings.RecentXgHome < 0 || settings.RecentXgAway < 0)
        {
            throw new ArgumentException("Recent-window xG cannot be negative.", nameof(settings));
        }

        if (settings.RecentXgHome > state.CumulativeXgHome || settings.RecentXgAway > state.CumulativeXgAway)
        {
            throw new ArgumentException(
                "Recent-window xG cannot exceed the cumulative xG it is part of. If you are reading a " +
                "per-half split, the recent window is the second half: total minus first half.",
                nameof(settings));
        }

        var weight = Math.Clamp(settings.RecencyWeight, 0.0, 1.0);
        var recentHome = settings.RecentXgHome!.Value / window;
        var recentAway = settings.RecentXgAway!.Value / window;

        var blendedHome = weight * recentHome + (1 - weight) * home;
        var blendedAway = weight * recentAway + (1 - weight) * away;

        steps.Add(
            $"Last {Num(window)} minutes weighted {Pct(weight)}: rate {Rate(home)} → {Rate(blendedHome)} home, " +
            $"{Rate(away)} → {Rate(blendedAway)} away.");

        return (blendedHome, blendedAway);
    }

    /// <summary>
    /// Bayesian shrinkage towards each side's prior. Written in xG rather than rate terms so the
    /// weighting by minutes played is explicit: the evidence is worth the minutes it took to gather.
    /// </summary>
    /// <param name="matchMinutes">How many minutes a match lasts, in the same minutes as the rate: a
    /// per-match prior is spread over these.</param>
    /// <param name="priorHome">Goals per match the home side is pulled towards.</param>
    /// <param name="priorAway">The same for the away side.</param>
    /// <param name="fromPreMatch">Whether those came from the pre-match model, for the explanation.</param>
    private static (double Home, double Away) ApplyShrinkage(
        double elapsed,
        double matchMinutes,
        double home,
        double away,
        double priorHome,
        double priorAway,
        bool fromPreMatch,
        List<string> steps)
    {
        var denominator = elapsed + PriorStrengthMinutes;

        var shrunkHome = (home * elapsed + PriorXg(priorHome, matchMinutes)) / denominator;
        var shrunkAway = (away * elapsed + PriorXg(priorAway, matchMinutes)) / denominator;

        var target = fromPreMatch
            ? $"this match's pre-match expectation of {Num(priorHome)} xG home and {Num(priorAway)} away"
            : $"a league-average {Num(LeagueAverageGoalsPerTeam)} xG per team";

        var evidenceWeight = elapsed / denominator;
        steps.Add(
            $"Pulled towards {target} " +
            $"({Pct(evidenceWeight)} weight on what this match has actually shown): " +
            $"rate {Rate(home)} → {Rate(shrunkHome)} home, {Rate(away)} → {Rate(shrunkAway)} away.");

        return (shrunkHome, shrunkAway);
    }

    /// <summary>A prior of so many goals per match, expressed as the xG it is worth in the blend.</summary>
    private static double PriorXg(double goalsPerMatch, double matchMinutes)
        => goalsPerMatch / matchMinutes * PriorStrengthMinutes;

    private static bool IsPositiveFinite(double value) => value > 0.0 && double.IsFinite(value);

    /// <summary>
    /// The leader attacks less, the chaser attacks more, and both effects grow as time runs out.
    /// </summary>
    private static (double Home, double Away) ApplyScoreState(
        LiveMatchState state,
        double home,
        double away,
        List<string> steps)
    {
        var lead = Math.Clamp(state.CurrentGoalsHome - state.CurrentGoalsAway, -MaxLeadConsidered, MaxLeadConsidered);
        if (lead == 0)
        {
            return (home, away);
        }

        // A one-goal lead at minute 20 changes almost nothing; the same lead at minute 85 changes how
        // both sides play completely. Read off the minute on the clock, which is what the players are
        // looking at, rather than the minutes of play elapsed.
        var lateness = Math.Min(1.0, (double)state.MinutePlayed / PoissonGoalModel.RegulationMinutes);
        var effect = ScoreStateEffectPerGoal * lateness * Math.Abs(lead);

        var leaderIsHome = lead > 0;
        var adjustedHome = home * (leaderIsHome ? 1 - effect : 1 + effect);
        var adjustedAway = away * (leaderIsHome ? 1 + effect : 1 - effect);

        var leaderName = leaderIsHome ? "Home" : "Away";
        var chaserName = leaderIsHome ? "away" : "home";
        steps.Add(
            $"{leaderName} leads by {Math.Abs(lead)} at minute {state.MinutePlayed}: leader's rate cut " +
            $"{Pct(effect)} and the {chaserName} side's raised {Pct(effect)}, since a side protecting a " +
            "lead attacks less and a side chasing attacks more.");

        return (adjustedHome, adjustedAway);
    }

    /// <summary>
    /// The side down to ten scores less and its opponent more, for as long as the match has left.
    /// </summary>
    /// <remarks>
    /// The correction is applied to the share of the rate that was built from eleven-a-side football
    /// only. The xG played since the card already shows the man down, and correcting it again would
    /// count the red card twice — at minute 85, a card shown at minute 10 is mostly in the numbers
    /// already, while one shown a minute ago is not in them at all. The prior is all eleven-a-side,
    /// so when shrinkage ran it counts as evidence from before the card.
    ///
    /// Written out: the blended rate is r11 × (s + (1 − s) × f), where s is the eleven-a-side share
    /// and f the red-card factor, and the rate the rest of the match is played at is r11 × f. A card
    /// with no minute is taken to be the newest thing that happened, which is when it matters most.
    /// </remarks>
    private static (double Home, double Away) ApplyRedCards(
        LiveMatchState state,
        bool shrunk,
        double home,
        double away,
        List<string> steps)
    {
        // Positive when the home side has more players on the pitch.
        var advantage = Math.Clamp(
            state.RedCardsAway - state.RedCardsHome, -MaxManAdvantageConsidered, MaxManAdvantageConsidered);
        if (advantage == 0)
        {
            return (home, away);
        }

        var men = Math.Abs(advantage);
        var minute = (double)state.MinutePlayed;
        var cardMinute = Math.Clamp(state.LastRedCardMinute ?? state.MinutePlayed, 0, state.MinutePlayed);
        var priorMinutes = shrunk ? PriorStrengthMinutes : 0.0;
        var elevenASide = (cardMinute + priorMinutes) / (minute + priorMinutes);

        var shortHanded = Unmixed(Math.Pow(ShortHandedRateFactor, men), elevenASide);
        var opponent = Unmixed(Math.Pow(AgainstShortHandedRateFactor, men), elevenASide);

        var homeShort = advantage < 0;
        var adjustedHome = home * (homeShort ? shortHanded : opponent);
        var adjustedAway = away * (homeShort ? opponent : shortHanded);

        var shortName = homeShort ? "Home" : "Away";
        var otherName = homeShort ? "away" : "home";
        var since = state.LastRedCardMinute is { } at ? $" since minute {at}" : string.Empty;
        steps.Add(
            $"{shortName} side down to {11 - men} men{since}: its rate cut {Pct(1 - shortHanded)} and the " +
            $"{otherName} side's raised {Pct(opponent - 1)}. A sending-off is worth about a third off one " +
            "side's scoring and a quarter on the other's; what the xG since the card already shows is not counted twice.");

        return (adjustedHome, adjustedAway);
    }

    /// <summary>The forward multiple, given a rate that already carries the factor on part of its evidence.</summary>
    private static double Unmixed(double factor, double elevenASideShare)
        => factor / (elevenASideShare + (1 - elevenASideShare) * factor);

    private static string? BuildWarning(double projectedHome, double projectedAway, int minute, bool adjusted)
    {
        var worst = Math.Max(projectedHome, projectedAway);
        if (worst < ImplausibleProjectedXg)
        {
            return null;
        }

        var side = projectedHome >= projectedAway ? "home" : "away";
        var warning =
            $"Held flat, the {side} side's current rate projects {Num(worst)} xG over ninety minutes — a " +
            $"total almost no match reaches. Reading that pace off {minute} minutes and running it to " +
            "full time is exactly where this model goes wrong.";

        return adjusted
            ? warning + " The pace corrections below are what keep that from reaching the price."
            : warning + " Pace corrections are switched OFF, so that projection is being used as-is.";
    }

    /// <summary>
    /// The health warning that belongs on a live result, worded for the correction that actually
    /// happened.
    /// </summary>
    /// <remarks>
    /// The spec's caveat says to treat the model's number as "a floor, not a ceiling, on scoring
    /// probability". That is true of the case it was written for — a quiet match that suddenly opens
    /// up — and flatly false of the opposite one. Told that Barcelona were 3-1 up on 2.38 xG after 36
    /// minutes, the flat model returns 76% on Over 6.5 and then advises the reader that the real
    /// number is probably higher. It is not; the market's 50% was nearer. A caveat that only ever
    /// points one way is worse than none, so this one reports the direction the correction went.
    /// </remarks>
    public static string CaveatFor(PaceAdjustment adjustment)
    {
        if (!adjustment.IsAdjusted)
        {
            return PoissonGoalModel.PaceCaveat;
        }

        var multiplier = adjustment.PaceMultiplier;

        if (multiplier < 0.95)
        {
            return
                $"Pace corrected DOWN to {Pct(multiplier)} of the raw clock average: an early scoring " +
                "burst is mostly small-sample noise, and matches that start hot rarely stay that hot. " +
                "Treat this as the middle of a range, and if you genuinely think this game is that " +
                "open, turn the corrections off and compare the two numbers.";
        }

        if (multiplier > 1.05)
        {
            return
                $"Pace corrected UP to {Pct(multiplier)} of the raw clock average: a quiet stretch is " +
                "not proof the match will stay quiet, and this model has no term for a game suddenly " +
                "opening up. Even corrected, read this as a floor rather than a ceiling.";
        }

        return adjustment.PriorFromPreMatch
            ? "Pace corrections barely moved the rate here — the match is running close to what the " +
              "pre-match numbers expected of these two teams. The remaining assumption is still that " +
              "it keeps doing so."
            : "Pace corrections barely moved the rate here — the match is running close to what a " +
              "league-average game would. The remaining assumption is still that it keeps doing so.";
    }

    private static string Rate(double perMinute)
        => (perMinute * PoissonGoalModel.RegulationMinutes).ToString("0.00", CultureInfo.InvariantCulture) + "/90";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(double fraction)
        => (fraction * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
}
