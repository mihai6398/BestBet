using System.Text.Json;
using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// What FotMob says about the parts of a match the minute alone cannot: which half it is, what the
/// fourth official's board showed, who has been sent off, and how the last twenty minutes went.
///
/// The fixtures are real responses, trimmed to the branches the reader walks and otherwise untouched:
/// Como - Manchester United (Champions League, 21.10.2026) captured live in first-half stoppage and
/// again at half time, and VfB Stuttgart - Atlético Madrid (20.10.2026) after the final whistle, with
/// Atlético's red card in it.
/// </summary>
public class FotMobStoppageReadingTests
{
    [Fact]
    public void First_half_stoppage_is_read_as_the_first_half_with_its_board()
    {
        // The clock says 45:46, which the paste rules make minute 46 — the same number the second
        // half shows a minute after the restart. The half markers are what tell them apart.
        var reading = Read("fotmob-first-half-stoppage");

        Assert.Equal(46, reading.Minute);
        Assert.Equal(MatchPeriod.FirstHalf, reading.Period);
        Assert.Equal(1, reading.AddedTimeFirstHalf);
        Assert.Null(reading.AddedTimeSecondHalf);
    }

    [Fact]
    public void Half_time_is_its_own_period_and_keeps_the_first_half_board()
    {
        var reading = Read("fotmob-half-time");

        Assert.Equal(45, reading.Minute);
        Assert.Equal(MatchPeriod.HalfTime, reading.Period);
        Assert.Equal(1, reading.AddedTimeFirstHalf);
    }

    [Fact]
    public void A_finished_match_has_no_period_but_keeps_its_boards_and_cards()
    {
        var reading = Read("fotmob-finished-red-card");

        Assert.Null(reading.Period);
        Assert.Null(reading.AddedTimeFirstHalf);
        Assert.Equal(4, reading.AddedTimeSecondHalf);
        Assert.Equal(0, reading.RedCardsHome);
        Assert.Equal(1, reading.RedCardsAway);
        Assert.Equal(64, reading.LastRedCardMinute);
    }

    [Fact]
    public void No_red_card_reads_as_eleven_against_eleven()
    {
        var reading = Read("fotmob-first-half-stoppage");

        Assert.Equal(0, reading.RedCardsHome);
        Assert.Equal(0, reading.RedCardsAway);
        Assert.Null(reading.LastRedCardMinute);
    }

    [Fact]
    public void The_recent_window_is_the_last_twenty_minutes_of_shots()
    {
        // Minute 46 of the first half: the window reaches back to minute 26. Como's shots at 35, 39
        // and 43 are in it (0.040 + 0.243 + 0.042); United's only one is the 0.035 in stoppage.
        var reading = Read("fotmob-first-half-stoppage");

        Assert.Equal(FotMobReading.RecentWindowMinutes, reading.RecentWindowMinutes);
        Assert.Equal(0.33, reading.RecentXgHome!.Value, 2);
        Assert.Equal(0.03, reading.RecentXgAway!.Value, 2);
    }

    [Fact]
    public void A_recent_window_never_claims_more_xG_than_the_match_total()
    {
        // The pace model refuses a window holding more than the whole match, rightly. Shot values are
        // rounded differently from the printed total, so the reader has to keep inside it itself.
        var reading = Read("fotmob-half-time");

        Assert.True(reading.RecentXgHome <= reading.XgHome);
        Assert.True(reading.RecentXgAway <= reading.XgAway);
    }

    [Fact]
    public void A_finished_match_has_no_recent_window()
    {
        Assert.Null(Read("fotmob-finished-red-card").RecentWindowMinutes);
    }

    [Fact]
    public void The_older_fixtures_without_a_shot_map_have_no_recent_window_rather_than_an_empty_one()
    {
        // An empty window would read as twenty dead minutes and pull the rate down. No shot list is
        // no information, and no information leaves the whole-match rate in charge.
        var reading = Read("fotmob-live-with-xg");

        Assert.Null(reading.RecentWindowMinutes);
        Assert.Null(reading.RecentXgHome);
    }

    [Fact]
    public void A_reading_feeds_the_model_without_being_refused()
    {
        // The end-to-end check: every new field the reader produces is one the model accepts.
        var reading = Read("fotmob-first-half-stoppage");
        var state = new LiveMatchState(
            reading.Minute!.Value, reading.XgHome!.Value, reading.XgAway!.Value,
            reading.GoalsHome!.Value, reading.GoalsAway!.Value, false,
            reading.Period, reading.AddedTimeFirstHalf, reading.AddedTimeSecondHalf,
            reading.RedCardsHome, reading.RedCardsAway, reading.LastRedCardMinute);
        var settings = new PaceSettings(
            RecentWindowMinutes: reading.RecentWindowMinutes,
            RecentXgHome: reading.RecentXgHome,
            RecentXgAway: reading.RecentXgAway);

        var pace = PaceModel.Adjust(state, settings);

        // Half a minute of this half left on a +1 board — held at the one minute the clock never goes
        // below while a half is still being played — then a whole half and its usual stoppage.
        Assert.Equal(
            MatchClock.MinimumMinutesLeft + 45 + MatchClock.ExpectedSecondHalfStoppage, pace.RemainingMinutes, 10);
        Assert.Contains(pace.Steps, s => s.Contains("Last 20 minutes"));
    }

    private static ProviderReading Read(string fixture)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{fixture}.json")));

        return FotMobReading.From(document, "ignored-when-the-response-carries-one");
    }
}
