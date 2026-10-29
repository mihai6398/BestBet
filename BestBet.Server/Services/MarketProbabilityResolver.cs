using System.Globalization;
using BestBet.Domain;

namespace BestBet.Services;

/// <param name="Probability">The number to compare against the de-vigged market price. Where the line
/// can push, this is conditional on no push — the correct fair value when the stake is returned.</param>
/// <param name="RawWinProbability">P(win) before any push adjustment. Equals
/// <paramref name="Probability"/> whenever <paramref name="PushProbability"/> is zero.</param>
/// <param name="PushProbability">P(stake returned). Non-zero only on whole goal lines.</param>
/// <param name="Description">How the number was derived, in a sentence, for display.</param>
public record MarketProbability(
    double Probability,
    double RawWinProbability,
    double PushProbability,
    string Description);

/// <summary>
/// Projects the remaining-goals distribution onto whichever market is being priced.
/// </summary>
public static class MarketProbabilityResolver
{
    public static MarketProbability Resolve(MarketKind kind, MarketSelectionSpec selection, GoalDistribution distribution)
        => kind switch
        {
            MarketKind.TotalGoals => TotalGoals(selection, distribution),
            MarketKind.NextGoal => NextGoal(selection, distribution),
            MarketKind.BothTeamsToScore => BothTeamsToScore(selection, distribution),
            MarketKind.Moneyline1X2 => Moneyline(selection, distribution),
            MarketKind.Handicap => Handicap(selection, distribution),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown market kind."),
        };

    private static MarketProbability TotalGoals(MarketSelectionSpec selection, GoalDistribution distribution)
    {
        if (selection.Side is not (MarketSelection.Over or MarketSelection.Under))
        {
            throw new ArgumentException($"{selection.Side} is not a total-goals selection.", nameof(selection));
        }

        var line = selection.Line
                   ?? throw new ArgumentException("A total-goals market needs a goal line.", nameof(selection));

        var pmf = distribution.FinalTotalPmf();
        double over = 0, under = 0, push = 0;
        for (var total = 0; total < pmf.Length; total++)
        {
            if (total > line) over += pmf[total];
            else if (total < line) under += pmf[total];
            else push += pmf[total];   // only reachable on a whole line
        }

        var isOver = selection.Side == MarketSelection.Over;
        var win = isOver ? over : under;
        var word = isOver ? "over" : "under";

        var description =
            $"P(final total {(isOver ? ">" : "<")} {Num(line)}) = {Pct(win)}, from {Num(distribution.ExpectedRemainingHome)} + " +
            $"{Num(distribution.ExpectedRemainingAway)} expected goals over the remaining {Num(distribution.RemainingMinutes)} minutes, " +
            $"added to the {distribution.CurrentGoalsHome + distribution.CurrentGoalsAway} already scored.";

        if (push > 0)
        {
            description +=
                $" Exactly {Num(line)} goals is a push worth {Pct(push)} — the {Pct(Conditional(win, push))} shown is " +
                $"conditional on no push, which is the fair value when the stake is returned. If your bookmaker settles " +
                $"exactly {Num(line)} as a loss for \"{word}\", use {Pct(win)} instead.";
        }

        return new MarketProbability(Conditional(win, push), win, push, description);
    }

    private static MarketProbability NextGoal(MarketSelectionSpec selection, GoalDistribution distribution)
    {
        // Two independent processes racing each other. Whichever fires first scores next, and the
        // chance it is the home team is just its share of the combined rate. "No further goal" is read
        // off the tables rather than as e^-λ, so it follows the spread when the rate carries one.
        var lambdaHome = distribution.ExpectedRemainingHome;
        var lambdaAway = distribution.ExpectedRemainingAway;
        var lambdaTotal = lambdaHome + lambdaAway;

        var none = distribution.RemainingHomePmf[0] * distribution.RemainingAwayPmf[0];
        var anyGoal = 1.0 - none;

        var (home, away) = lambdaTotal <= 0
            ? (0.0, 0.0)
            : (lambdaHome / lambdaTotal * anyGoal, lambdaAway / lambdaTotal * anyGoal);

        var (win, who) = selection.Side switch
        {
            MarketSelection.NextGoalHome => (home, "home scores next"),
            MarketSelection.NextGoalAway => (away, "away scores next"),
            MarketSelection.NextGoalNone => (none, "no further goal"),
            _ => throw new ArgumentException($"{selection.Side} is not a next-goal selection.", nameof(selection)),
        };

        var homeShare = lambdaTotal <= 0 ? 0.0 : lambdaHome / lambdaTotal;
        var awayShare = lambdaTotal <= 0 ? 0.0 : lambdaAway / lambdaTotal;

        var description =
            $"P({who}) = {Pct(win)}. Over the remaining {Num(distribution.RemainingMinutes)} minutes the model expects " +
            $"{Num(lambdaHome)} home and {Num(lambdaAway)} away goals, so P(no further goal) = {Pct(none)} and the " +
            $"remaining {Pct(anyGoal)} splits {Pct(homeShare)} / {Pct(awayShare)} by scoring rate.";

        return new MarketProbability(win, win, 0.0, description);
    }

    private static MarketProbability BothTeamsToScore(MarketSelectionSpec selection, GoalDistribution distribution)
    {
        // A team that has already scored is home and dry at probability 1 — no model needed.
        var homeScores = distribution.CurrentGoalsHome > 0 ? 1.0 : 1.0 - distribution.RemainingHomePmf[0];
        var awayScores = distribution.CurrentGoalsAway > 0 ? 1.0 : 1.0 - distribution.RemainingAwayPmf[0];
        var yes = homeScores * awayScores;

        var win = selection.Side switch
        {
            MarketSelection.Yes => yes,
            MarketSelection.No => 1.0 - yes,
            _ => throw new ArgumentException($"{selection.Side} is not a both-teams-to-score selection.", nameof(selection)),
        };

        var description =
            $"P(both teams score) = {Pct(yes)}, as {Pct(homeScores)} (home) x {Pct(awayScores)} (away), treating the two " +
            $"sides as independent. Current score {distribution.CurrentGoalsHome}-{distribution.CurrentGoalsAway}.";

        return new MarketProbability(win, win, 0.0, description);
    }

    private static MarketProbability Moneyline(MarketSelectionSpec selection, GoalDistribution distribution)
    {
        var (homeWin, draw, awayWin) = ResultProbabilities(distribution, 0.0);

        var win = selection.Side switch
        {
            MarketSelection.Home => homeWin,
            MarketSelection.Draw => draw,
            MarketSelection.Away => awayWin,
            _ => throw new ArgumentException($"{selection.Side} is not a 1X2 selection.", nameof(selection)),
        };

        var description =
            $"Final-result split from the current {distribution.CurrentGoalsHome}-{distribution.CurrentGoalsAway} score plus " +
            $"{Num(distribution.ExpectedRemainingHome)} / {Num(distribution.ExpectedRemainingAway)} expected goals over the " +
            $"remaining {Num(distribution.RemainingMinutes)} minutes: home {Pct(homeWin)}, draw {Pct(draw)}, away {Pct(awayWin)}.";

        return new MarketProbability(win, win, 0.0, description);
    }

    private static MarketProbability Handicap(MarketSelectionSpec selection, GoalDistribution distribution)
    {
        var line = selection.Line
                   ?? throw new ArgumentException("A handicap market needs a line.", nameof(selection));

        var forHome = selection.Side switch
        {
            MarketSelection.HandicapHome => true,
            MarketSelection.HandicapAway => false,
            _ => throw new ArgumentException($"{selection.Side} is not a handicap selection.", nameof(selection)),
        };

        // The line is signed from the selected team's point of view, so applying it to the home margin
        // means flipping its sign when the selection is the away team.
        var homeSideLine = forHome ? line : -line;
        var (homeCovers, push, awayCovers) = ResultProbabilities(distribution, homeSideLine);
        var win = forHome ? homeCovers : awayCovers;

        var description =
            $"P({(forHome ? "home" : "away")} covers {Signed(line)}) = {Pct(win)} against the projected final score.";

        if (push > 0)
        {
            description +=
                $" A whole line pushes {Pct(push)} of the time; {Pct(Conditional(win, push))} is the value conditional " +
                "on no push, which is what a returned stake is worth.";
        }

        return new MarketProbability(Conditional(win, push), win, push, description);
    }

    /// <summary>
    /// Joint distribution over final scores, collapsed onto (home margin + line) being positive,
    /// zero, or negative. A line of 0 gives the plain 1X2 split.
    /// </summary>
    private static (double HomeSide, double Level, double AwaySide) ResultProbabilities(
        GoalDistribution distribution,
        double homeSideLine)
    {
        double homeSide = 0, level = 0, awaySide = 0;

        var firstHome = distribution.CurrentGoalsHome;
        var firstAway = distribution.CurrentGoalsAway;
        var lastHome = firstHome + distribution.MaxGoals;
        var lastAway = firstAway + distribution.MaxGoals;

        for (var home = firstHome; home <= lastHome; home++)
        {
            var pHome = distribution.FinalHomeGoalsProbability(home);
            if (pHome == 0.0) continue;

            for (var away = firstAway; away <= lastAway; away++)
            {
                var joint = pHome * distribution.FinalAwayGoalsProbability(away);
                if (joint == 0.0) continue;

                var margin = home - away + homeSideLine;
                if (margin > 0) homeSide += joint;
                else if (margin < 0) awaySide += joint;
                else level += joint;
            }
        }

        return (homeSide, level, awaySide);
    }

    /// <summary>Win probability conditional on the market not pushing. Identity when nothing can push.</summary>
    private static double Conditional(double win, double push)
        => push <= 0.0 ? win : (push >= 1.0 ? 0.0 : win / (1.0 - push));

    private static string Pct(double p) => (p * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Signed(double value) => (value >= 0 ? "+" : "") + Num(value);
}
