using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// How much football is left, once the referee's added time is counted.
/// </summary>
public class MatchClockTests
{
    private static LiveMatchState At(
        int minute,
        MatchPeriod? period = null,
        int? boardFirst = null,
        int? boardSecond = null)
        => new(minute, 0.5, 0.5, 0, 0, false, period, boardFirst, boardSecond);

    [Fact]
    public void Early_in_the_first_half_both_halves_stoppage_is_still_to_come()
    {
        // 15 to half time, the usual 3 the first half runs over, 45, and the usual 4.5 after 90.
        Assert.Equal(67.5, MatchClock.RemainingMinutes(At(30)), 10);
        Assert.Equal(30, MatchClock.ElapsedMinutes(At(30)), 10);
    }

    [Fact]
    public void A_board_replaces_the_average_and_the_half_runs_a_little_past_it()
    {
        // "45+2" with +3 on the board: a minute and a half of this half, then the whole second one.
        var state = At(47, MatchPeriod.FirstHalf, boardFirst: 3);

        Assert.Equal(1.5 + 45 + 4.5, MatchClock.RemainingMinutes(state), 10);
        Assert.Equal(47, MatchClock.ElapsedMinutes(state), 10);
    }

    [Fact]
    public void Without_a_period_minute_47_is_read_as_the_second_half()
    {
        // The ambiguity the period exists for. A typed 47 is far more often the second half than
        // first-half stoppage, so that is the reading when nothing says otherwise.
        Assert.Equal(43 + 4.5, MatchClock.RemainingMinutes(At(47)), 10);
    }

    [Fact]
    public void Half_time_has_the_second_half_ahead_and_the_first_half_behind()
    {
        var state = At(45, MatchPeriod.HalfTime, boardFirst: 1);

        Assert.Equal(45 + 4.5, MatchClock.RemainingMinutes(state), 10);
        Assert.Equal(45 + 1.5, MatchClock.ElapsedMinutes(state), 10);
    }

    [Fact]
    public void The_second_half_counts_the_first_halfs_stoppage_as_played()
    {
        // The xG on a clock reading 60 was produced in 60 minutes plus what the first half ran over.
        Assert.Equal(62.5, MatchClock.ElapsedMinutes(At(60, MatchPeriod.SecondHalf, boardFirst: 2)), 10);
        Assert.Equal(63, MatchClock.ElapsedMinutes(At(60)), 10);
    }

    [Theory]
    [InlineData(80, 14.5)]
    [InlineData(85, 9.5)]
    [InlineData(86, 8.5)]
    [InlineData(90, 4.5)]
    public void Late_in_the_match_the_usual_stoppage_is_counted_before_the_board_goes_up(int minute, double expected)
        => Assert.Equal(expected, MatchClock.RemainingMinutes(At(minute)), 10);

    [Fact]
    public void There_is_no_cliff_at_minute_85()
    {
        // The spec's clock jumps from 5 minutes left at 85 to 9 at 86. Time only runs one way.
        var left = Enumerable.Range(75, 16).Select(minute => MatchClock.RemainingMinutes(At(minute))).ToList();

        for (var i = 1; i < left.Count; i++)
        {
            Assert.Equal(1.0, left[i - 1] - left[i], 10);
        }
    }

    [Fact]
    public void A_big_board_at_90_is_what_the_rest_of_the_match_is_priced_on()
    {
        Assert.Equal(6.5, MatchClock.RemainingMinutes(At(90, MatchPeriod.SecondHalf, boardSecond: 6)), 10);
        Assert.Equal(3.5, MatchClock.RemainingMinutes(At(93, MatchPeriod.SecondHalf, boardSecond: 6)), 10);
    }

    [Fact]
    public void Past_the_board_a_match_still_being_played_keeps_a_minute()
    {
        // 90+7 on a +4 board: the referee is playing on for something, and nothing in the model
        // should claim another goal is impossible in a match that is visibly still going.
        Assert.Equal(MatchClock.MinimumMinutesLeft, MatchClock.RemainingMinutes(At(97, MatchPeriod.SecondHalf, boardSecond: 4)), 10);
    }

    [Fact]
    public void A_whole_match_is_ninety_minutes_plus_both_halves_usual_stoppage()
    {
        Assert.Equal(97.5, MatchClock.TypicalMatchMinutes, 10);

        // And the clock adds up: whatever the minute, played plus left is the whole match.
        foreach (var minute in new[] { 1, 20, 44, 50, 75, 89 })
        {
            var state = At(minute);
            Assert.Equal(97.5, MatchClock.ElapsedMinutes(state) + MatchClock.RemainingMinutes(state), 10);
        }
    }

    [Fact]
    public void The_explanation_quotes_the_specs_clock_beside_this_one()
    {
        var line = MatchClock.Describe(At(88, MatchPeriod.SecondHalf, boardSecond: 7));

        Assert.Contains("9.5 minutes left rather than 7", line);
        Assert.Contains("+7 at 90", line);
    }
}
