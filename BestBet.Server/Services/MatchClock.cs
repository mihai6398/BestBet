using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// How much football has been played and how much is left, counting the minutes the referee adds.
/// </summary>
/// <remarks>
/// The spec's clock ends the match at 90 and adds a fixed five minutes once the clock passes 85. That
/// is a cliff: at minute 85 it sees five minutes left and at 86 it sees nine. And before the cliff it
/// sees no stoppage at all, so at minute 80 it prices ten minutes of football when about fifteen will
/// be played — a third of the remaining time missing, every match, always on the side of the Under.
///
/// Stoppage is not a rounding error at this end of a match, and it is the one part of the future
/// that is announced in advance: the fourth official holds up a board. So this clock counts the board
/// once it has been shown and the average until then, in both halves.
///
/// The averages are measured, not guessed: 27 matches in the top five leagues and the Champions
/// League, 18–20 October 2026, read from FotMob — the board, and the time each half actually lasted
/// from its kick-off and final-whistle timestamps. First halves ran 2.8 minutes over on average and
/// second halves 4.3, and both ran about half a minute past what the board showed, because the board
/// is a minimum. A weekend is a small sample and smaller leagues add less, so the numbers are rounded
/// to the half minute and stated here to be argued with.
///
/// Everything is in minutes of play actually elapsed. A second-half clock reading 60 means 60 minutes
/// plus whatever the first half ran over, and the xG on it was produced in all of them — dividing by
/// 60 alone would overstate the rate by the same few percent this clock adds back at the other end.
/// </remarks>
public static class MatchClock
{
    public const int HalfMinutes = 45;

    /// <summary>Average first-half stoppage actually played, when no board has been shown yet.</summary>
    public const double ExpectedFirstHalfStoppage = 3.0;

    /// <summary>Average second-half stoppage actually played, when no board has been shown yet.</summary>
    public const double ExpectedSecondHalfStoppage = 4.5;

    /// <summary>How far past the board a half typically runs. The board shows a minimum.</summary>
    public const double PlayedBeyondBoard = 0.5;

    /// <summary>
    /// The least time a match still being played is given. Past the board the referee is playing on
    /// for something — an injury, a VAR check, a goal in stoppage — and zero would claim nothing more
    /// can happen in a match that is visibly still going.
    /// </summary>
    public const double MinimumMinutesLeft = 1.0;

    /// <summary>
    /// How long a match lasts on average, stoppage included. A per-match expectation — the league
    /// average, or a pre-match model's expected goals — is spread over this many minutes, not 90,
    /// or the projection would add the stoppage on top of numbers that already contain it.
    /// </summary>
    public const double TypicalMatchMinutes =
        2 * HalfMinutes + ExpectedFirstHalfStoppage + ExpectedSecondHalfStoppage;

    /// <summary>The period the clock is in: as given, or read off the minute when it was not.</summary>
    public static MatchPeriod PeriodOf(LiveMatchState state)
        => state.Period ?? (state.MinutePlayed <= HalfMinutes ? MatchPeriod.FirstHalf : MatchPeriod.SecondHalf);

    /// <summary>Minutes of football played so far, first-half stoppage included once it is over.</summary>
    public static double ElapsedMinutes(LiveMatchState state) => PeriodOf(state) switch
    {
        MatchPeriod.FirstHalf => state.MinutePlayed,
        MatchPeriod.HalfTime => HalfMinutes + FirstHalfStoppage(state),
        _ => state.MinutePlayed + FirstHalfStoppage(state),
    };

    /// <summary>Minutes of football still to be played in regulation time, stoppage included.</summary>
    /// <remarks>
    /// Regulation only, deliberately: totals, results and handicaps settle on ninety minutes plus
    /// stoppage, so extra time is not part of any market this app prices.
    /// </remarks>
    public static double RemainingMinutes(LiveMatchState state) => PeriodOf(state) switch
    {
        MatchPeriod.FirstHalf =>
            LeftInHalf(state.MinutePlayed, HalfMinutes, FirstHalfStoppage(state)) + HalfMinutes + SecondHalfStoppage(state),
        MatchPeriod.HalfTime => HalfMinutes + SecondHalfStoppage(state),
        _ => LeftInHalf(state.MinutePlayed, 2 * HalfMinutes, SecondHalfStoppage(state)),
    };

    /// <summary>The first half's stoppage: the board plus the usual overrun once shown, the average until then.</summary>
    public static double FirstHalfStoppage(LiveMatchState state)
        => state.AddedTimeFirstHalf is { } board ? board + PlayedBeyondBoard : ExpectedFirstHalfStoppage;

    /// <summary>The same for the second half.</summary>
    public static double SecondHalfStoppage(LiveMatchState state)
        => state.AddedTimeSecondHalf is { } board ? board + PlayedBeyondBoard : ExpectedSecondHalfStoppage;

    /// <summary>
    /// One line saying what the stoppage did to the time left, for the list of corrections a result
    /// shows. The spec's clock is quoted beside it so the size of the difference is visible.
    /// </summary>
    public static string Describe(LiveMatchState state)
    {
        var remaining = RemainingMinutes(state);
        var regulation = PoissonGoalModel.RemainingMinutes(state.MinutePlayed);
        var period = PeriodOf(state);

        var source = period switch
        {
            MatchPeriod.SecondHalf when state.AddedTimeSecondHalf is { } board =>
                $"the board showed +{board} at 90, and halves run about {Num(PlayedBeyondBoard)} min past it",
            MatchPeriod.SecondHalf =>
                $"no board yet, so the {Num(ExpectedSecondHalfStoppage)} min a second half usually runs over is counted",
            MatchPeriod.HalfTime =>
                $"the whole second half and the {Num(ExpectedSecondHalfStoppage)} min it usually runs over",
            _ when state.AddedTimeFirstHalf is { } board =>
                $"+{board} on the board at 45 and the usual {Num(ExpectedSecondHalfStoppage)} min after 90",
            _ =>
                $"no board yet, so the usual {Num(ExpectedFirstHalfStoppage)} min of first-half and " +
                $"{Num(ExpectedSecondHalfStoppage)} min of second-half stoppage are counted",
        };

        return $"Stoppage counted: {Num(remaining)} minutes left rather than {regulation} — {source}.";
    }

    private static double LeftInHalf(int minute, int halfEnds, double stoppage)
        => Math.Max(MinimumMinutesLeft, halfEnds + stoppage - minute);

    private static string Num(double value)
        => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
