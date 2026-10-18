using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// Whether two labels name the same match.
///
/// These cases are the same ones <c>liveFeed.test.ts</c> runs against the TypeScript half, on
/// purpose: the two implementations decide the same question on either side of the wire, and a drift
/// between them would show as a reading for a match whose open bets never get re-priced. Keeping the
/// case list identical is what makes that drift a failing test instead of a quiet gap.
/// </summary>
public class MatchNameMatchingTests
{
    [Fact]
    public void A_label_matches_itself()
        => Assert.True(MatchNameMatching.SameMatch(
            "FC Arouca - CF Estrela Amadora", "FC Arouca - CF Estrela Amadora"));

    [Fact]
    public void A_shortened_label_matches_the_full_one()
        => Assert.True(MatchNameMatching.SameMatch(
            "Arouca - Estrela Amadora", "FC Arouca - CF Estrela Amadora"));

    [Fact]
    public void Diacritics_and_punctuation_are_ignored()
        => Assert.True(MatchNameMatching.SameMatch("Atlético Madrid – Betis", "Atletico Madrid - Betis"));

    [Fact]
    public void A_URL_slug_matches_the_names_a_provider_prints()
    {
        // The case this half exists for: a Sofascore link gives up nothing but its slug, and the
        // match has to be found at FotMob by name alone.
        Assert.True(MatchNameMatching.SameMatch("arouca porto", "FC Arouca - FC Porto"));
        Assert.True(MatchNameMatching.SameMatch("estrela amadora arouca", "CF Estrela - FC Arouca"));
    }

    [Fact]
    public void Two_different_matches_are_refused()
    {
        // The one that matters. Priced against another match's clock, a saved bet gets a confident
        // number about a game it has nothing to do with.
        Assert.False(MatchNameMatching.SameMatch("Arsenal - Chelsea", "FC Arouca - CF Estrela Amadora"));
    }

    [Fact]
    public void Two_matches_that_merely_share_one_club_are_refused()
        => Assert.False(MatchNameMatching.SameMatch("Arsenal - Chelsea", "Arsenal - Tottenham"));

    [Fact]
    public void The_filler_half_of_a_club_name_does_not_match_on_its_own()
    {
        // "FC ... City" would otherwise pair up half the fixtures in Europe.
        Assert.False(MatchNameMatching.SameMatch("FC Porto - Sporting CP", "FC Basel - Sporting Lisboa"));
    }

    [Fact]
    public void A_single_distinctive_word_is_accepted_when_that_is_all_one_side_has()
        => Assert.True(MatchNameMatching.SameMatch("Arouca", "FC Arouca - CF Estrela Amadora"));

    [Fact]
    public void Nothing_to_compare_is_a_no()
    {
        Assert.False(MatchNameMatching.SameMatch("", "Arsenal - Chelsea"));
        Assert.False(MatchNameMatching.SameMatch(null, null));
        Assert.False(MatchNameMatching.SameMatch("FC - CF", "FC Arouca - CF Estrela Amadora"));
    }

    [Fact]
    public void How_many_names_two_labels_share_is_what_ranks_one_day_s_fixtures()
    {
        // The gate says who is eligible; this says which of several eligible fixtures to read. Both
        // of these pass the gate, and only one of them is the match that was asked for.
        Assert.Equal(2, MatchNameMatching.SharedWords("arouca porto", "FC Arouca - FC Porto"));
        Assert.Equal(1, MatchNameMatching.SharedWords("arouca porto", "FC Porto - Sporting CP"));
    }
}
