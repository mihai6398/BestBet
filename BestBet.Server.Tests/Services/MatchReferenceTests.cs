using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// Reading a match link or id, before anything is fetched.
///
/// The Sofascore cases are the ones with teeth. Their URL carries their own id and it is deliberately
/// thrown away: handing that number to FotMob would read a real clock off a real match that is not
/// the one the user is betting on, and nothing downstream could tell the difference. So only the
/// names survive the parse.
/// </summary>
public class MatchReferenceTests
{
    [Fact]
    public void A_bare_id_is_taken_as_a_FotMob_id()
    {
        var reference = MatchReference.Parse("5881191");

        Assert.Equal(MatchProvider.FotMob, reference!.Value.Provider);
        Assert.Equal("5881191", reference.Value.FotMobId);
    }

    [Fact]
    public void A_live_only_id_is_long_and_still_an_id()
    {
        // FotMob's ids for matches it only covers live run to ten digits.
        Assert.Equal("1000022162", MatchReference.Parse("1000022162")!.Value.FotMobId);
    }

    [Fact]
    public void A_short_FotMob_link_gives_up_its_id()
    {
        Assert.Equal("5881191", MatchReference.Parse("https://www.fotmob.com/match/5881191")!.Value.FotMobId);
    }

    [Fact]
    public void A_short_FotMob_link_with_the_slug_after_it_still_gives_up_its_id()
    {
        var reference = MatchReference.Parse(
            "https://www.fotmob.com/match/5881191/eintracht-frankfurt-vs-1-fc-koln");

        Assert.Equal("5881191", reference!.Value.FotMobId);
    }

    [Fact]
    public void A_long_FotMob_link_keeps_its_id_in_the_fragment()
    {
        var reference = MatchReference.Parse(
            "https://www.fotmob.com/matches/eintracht-frankfurt-vs-1-fc-koln/2mn1g7#5881191");

        Assert.Equal(MatchProvider.FotMob, reference!.Value.Provider);
        Assert.Equal("5881191", reference.Value.FotMobId);
    }

    [Fact]
    public void A_fragment_carrying_more_than_the_id_is_still_read()
    {
        // FotMob appends the open tab to the fragment when you share from inside a match.
        Assert.Equal("5881191", MatchReference.Parse(
            "https://www.fotmob.com/matches/frankfurt-vs-koln/2mn1g7#5881191:tab:stats")!.Value.FotMobId);
    }

    [Fact]
    public void A_tracking_query_glued_onto_a_link_changes_nothing()
    {
        Assert.Equal("5881191", MatchReference.Parse(
            "https://www.fotmob.com/match/5881191?utm_source=share")!.Value.FotMobId);
    }

    [Fact]
    public void A_link_copied_without_its_scheme_is_still_a_link()
    {
        Assert.Equal("5881191", MatchReference.Parse("www.fotmob.com/match/5881191")!.Value.FotMobId);
    }

    [Fact]
    public void A_Sofascore_link_keeps_the_team_names_and_drops_their_id()
    {
        var reference = MatchReference.Parse(
            "https://www.sofascore.com/football/match/arouca-porto/IdbsNcb#id:12345678");

        Assert.Equal(MatchProvider.Sofascore, reference!.Value.Provider);
        Assert.Null(reference.Value.FotMobId);
        Assert.Equal("arouca porto", reference.Value.TeamsLabel);
    }

    [Fact]
    public void A_Sofascore_slug_with_multi_word_clubs_is_kept_whole()
    {
        // There is no honest split point: the slug joins the two names with the same hyphen it uses
        // inside them. The name matching works on a bag of words, so it does not need one.
        var reference = MatchReference.Parse(
            "https://www.sofascore.com/football/match/estrela-amadora-arouca/pQbsCpb");

        Assert.Equal("estrela amadora arouca", reference!.Value.TeamsLabel);
    }

    [Fact]
    public void An_older_Sofascore_link_without_the_sport_in_it_still_parses()
    {
        var reference = MatchReference.Parse("https://www.sofascore.com/arouca-porto/IdbsNcb");

        Assert.Equal(MatchProvider.Sofascore, reference!.Value.Provider);
        Assert.Equal("arouca porto", reference.Value.TeamsLabel);
    }

    [Fact]
    public void What_is_not_a_match_link_is_refused_rather_than_guessed_at()
    {
        Assert.Null(MatchReference.Parse(null));
        Assert.Null(MatchReference.Parse(""));
        Assert.Null(MatchReference.Parse("   "));
        Assert.Null(MatchReference.Parse("Arouca - Porto"));
        Assert.Null(MatchReference.Parse("https://www.flashscore.ro/meci/abc123/"));
        Assert.Null(MatchReference.Parse("https://www.fotmob.com/leagues/54/overview/bundesliga"));
    }

    [Fact]
    public void A_number_too_short_to_be_a_match_id_is_refused()
    {
        // Far likelier to be a minute or a score typed into the wrong box than a fixture.
        Assert.Null(MatchReference.Parse("63"));
        Assert.Null(MatchReference.Parse("1234"));
    }
}
