using System.Text.Json;
using BestBet.Contracts;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// Reading a match off FotMob's <c>matchDetails</c> response.
///
/// Every fixture here is a response captured from the real API on 17.10.2026, trimmed to the
/// branches the reader walks and otherwise untouched. Nothing in this file goes near the network:
/// a test that needs a live match to pass stops being a test the day the fixture list changes.
///
/// The one stitched fixture is <c>fotmob-live-with-xg.json</c>: the Bundesliga match as captured,
/// with the running clock of a match that genuinely was in progress dropped in place of its
/// full-time marker. Nothing else is touched — the score, the teams and the stats are all its own.
/// No top-flight match was live when these were captured, and a live clock over a league that
/// computes xG is the combination this app spends almost all of its time reading, so it is the one
/// case worth constructing rather than leaving untested.
/// </summary>
public class FotMobReadingTests
{
    [Fact]
    public void A_live_match_gives_up_the_minute_the_score_and_the_xG()
    {
        var reading = Read("fotmob-live-with-xg");

        Assert.Equal("Eintracht Frankfurt - 1. FC Köln", reading.MatchLabel);
        Assert.Equal(63, reading.Minute);
        Assert.Equal(1, reading.GoalsHome);
        Assert.Equal(0, reading.GoalsAway);
        Assert.Equal(1.34, reading.XgHome);
        Assert.Equal(0.65, reading.XgAway);
        Assert.Empty(reading.Missing);
    }

    [Fact]
    public void The_clock_is_rounded_the_way_the_paste_parser_rounds_it()
    {
        // "62:08" is the 63rd minute, because any seconds at all mean that minute is being played.
        // If this ever disagreed with statsPaste.ts, a bet watched through the link would be priced
        // against a clock one minute off the same bet watched through the bookmarklet — small,
        // silent, and in the projection of every remaining minute.
        Assert.Equal(63, Read("fotmob-live-with-xg").Minute);
    }

    [Fact]
    public void A_live_match_is_reported_as_ongoing_and_not_finished()
    {
        var reading = Read("fotmob-live-with-xg");

        Assert.True(reading.Ongoing);
        Assert.False(reading.Finished);
    }

    [Fact]
    public void The_extra_stats_arrive_under_the_keys_the_client_already_knows()
    {
        // The ids are the client's own, from statsPaste.ts, so a fetched reading can be shown by the
        // same panel that shows a pasted one — in the user's language rather than as provider keys.
        var extras = Read("fotmob-live-with-xg").Extras.ToDictionary(row => row.Id);

        Assert.Equal("7", extras["shotsOnTarget"].Home);
        Assert.Equal("3", extras["shotsOnTarget"].Away);
        Assert.Equal("15", extras["shots"].Home);
        Assert.Equal("56", extras["possession"].Home);
        Assert.Equal("2", extras["bigChances"].Home);
        Assert.Equal("7", extras["corners"].Home);
        Assert.Equal("3", extras["saves"].Home);
        Assert.Equal("8", extras["fouls"].Home);
        Assert.Equal("2", extras["yellowCards"].Home);
    }

    [Fact]
    public void Nothing_is_reported_that_the_client_has_no_name_for()
    {
        // The response carries forty-odd stats. Carrying the ones statsPaste.ts does not recognise
        // would fill the panel with rows the interface can only label in English.
        var known = new[]
        {
            "shotsOnTarget", "bigChances", "shots", "possession",
            "corners", "saves", "fouls", "yellowCards",
        };

        Assert.All(Read("fotmob-live-with-xg").Extras, row => Assert.Contains(row.Id, known));
    }

    [Fact]
    public void A_league_without_xG_says_so_instead_of_guessing_a_number()
    {
        // The whole reason the bookmarklet stays. FotMob computes xG for the big leagues and not for
        // this one, and xG is the field the model is built on — so a plausible-looking stand-in here
        // would size a real stake on something nobody measured.
        var reading = Read("fotmob-live-no-xg");

        Assert.Equal("Club Bolivar - Olimpia", reading.MatchLabel);
        Assert.Equal(63, reading.Minute);
        Assert.Equal(1, reading.GoalsHome);
        Assert.Equal(2, reading.GoalsAway);
        Assert.Null(reading.XgHome);
        Assert.Null(reading.XgAway);
        Assert.Equal(["xgHome", "xgAway"], reading.Missing);
    }

    [Fact]
    public void A_league_without_xG_still_gives_up_everything_else()
    {
        // Unusable for the model is not the same as empty. These are what the user checks the found
        // match against before going back to the bookmarklet for it.
        var extras = Read("fotmob-live-no-xg").Extras.ToDictionary(row => row.Id);

        Assert.Equal("2", extras["shotsOnTarget"].Home);
        Assert.Equal("10", extras["shotsOnTarget"].Away);
        Assert.Equal("45", extras["possession"].Home);
    }

    [Fact]
    public void A_match_that_has_not_kicked_off_has_nothing_to_read()
    {
        // No clock, no scoreline, no stats block at all. Reported as missing rather than as nil-nil
        // in the first minute, which is what a defaulted reading would look like.
        var reading = Read("fotmob-not-started");

        Assert.Equal("Everton - Chelsea", reading.MatchLabel);
        Assert.Null(reading.Minute);
        Assert.Null(reading.GoalsHome);
        Assert.Null(reading.GoalsAway);
        Assert.Null(reading.XgHome);
        Assert.Equal(["minute", "xgHome", "xgAway"], reading.Missing);
        Assert.Empty(reading.Extras);
        Assert.False(reading.Ongoing);
    }

    [Fact]
    public void A_finished_match_reads_its_minute_off_the_reason_rather_than_a_clock()
    {
        // Once the match ends there is no liveTime at all, only reason: "FT". Worth reading anyway —
        // a bet watched to the final whistle should see 90 and not a blank.
        var reading = Read("fotmob-finished-with-xg");

        Assert.Equal(90, reading.Minute);
        Assert.True(reading.Finished);
        Assert.False(reading.Ongoing);
        Assert.Empty(reading.Missing);
    }

    [Fact]
    public void The_heading_row_that_shares_a_stat_s_key_does_not_hide_the_stat()
    {
        // Each stats group opens with a heading carrying the SAME key as the real stat and a pair of
        // nulls for its values. Read at face value it would claim this Bundesliga match has no xG —
        // which is exactly the signal that means "go back to the bookmarklet".
        var reading = Read("fotmob-finished-with-xg");

        Assert.Equal(1.34, reading.XgHome);
        Assert.Empty(reading.Missing);
    }

    [Fact]
    public void The_link_back_to_the_match_is_built_from_the_id_the_response_carries()
    {
        var reading = Read("fotmob-live-no-xg");

        Assert.Equal("1000022162", reading.MatchId);
        Assert.Equal("https://www.fotmob.com/match/1000022162", reading.Url);
        Assert.Equal(FotMobClient.DisplayName, reading.Provider);
    }

    private static ProviderReading Read(string fixture)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{fixture}.json")));

        return FotMobReading.From(document, "ignored-when-the-response-carries-one");
    }
}
