using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class MarketSelectionParserTests
{
    [Theory]
    [InlineData("Sub 4", MarketSelection.Under, 4.0)]
    [InlineData("Sub 3.5", MarketSelection.Under, 3.5)]
    [InlineData("Peste 2.5", MarketSelection.Over, 2.5)]
    [InlineData("Over 1.5", MarketSelection.Over, 1.5)]
    [InlineData("Under 4", MarketSelection.Under, 4.0)]
    [InlineData("Peste 2,5", MarketSelection.Over, 2.5)]   // comma decimal separator
    public void Total_goals_labels_are_read_in_either_language(string label, MarketSelection side, double line)
    {
        var parsed = MarketSelectionParser.Parse(MarketKind.TotalGoals, label);

        Assert.Equal(side, parsed.Side);
        Assert.Equal(line, parsed.Line);
    }

    [Theory]
    [InlineData("Da", MarketSelection.Yes)]
    [InlineData("Yes", MarketSelection.Yes)]
    [InlineData("GG", MarketSelection.Yes)]
    [InlineData("Nu", MarketSelection.No)]
    [InlineData("No", MarketSelection.No)]
    public void Both_teams_to_score_labels_are_read_in_either_language(string label, MarketSelection expected)
        => Assert.Equal(expected, MarketSelectionParser.Parse(MarketKind.BothTeamsToScore, label).Side);

    [Theory]
    [InlineData("Primul gol: 2", MarketSelection.NextGoalAway)]
    [InlineData("Primul gol: 1", MarketSelection.NextGoalHome)]
    [InlineData("Next goal: Home", MarketSelection.NextGoalHome)]
    [InlineData("Niciun gol", MarketSelection.NextGoalNone)]
    public void Next_goal_labels_are_read_from_the_team_index_or_the_word(string label, MarketSelection expected)
        => Assert.Equal(expected, MarketSelectionParser.Parse(MarketKind.NextGoal, label).Side);

    [Theory]
    [InlineData("1", MarketSelection.Home)]
    [InlineData("X", MarketSelection.Draw)]
    [InlineData("2", MarketSelection.Away)]
    [InlineData("Egal", MarketSelection.Draw)]
    public void Moneyline_labels_are_read_from_the_standard_1X2_tokens(string label, MarketSelection expected)
        => Assert.Equal(expected, MarketSelectionParser.Parse(MarketKind.Moneyline1X2, label).Side);

    [Fact]
    public void A_bare_team_name_is_refused_with_an_instruction_rather_than_guessed_at()
    {
        // The model knows "home" and "away"; it has never heard of Sevilla. Guessing here would size
        // a stake against the wrong side of the market.
        var ex = Assert.Throws<ArgumentException>(
            () => MarketSelectionParser.Parse(MarketKind.Moneyline1X2, "Sevilla FC"));

        Assert.Contains("explicitly", ex.Message);
    }

    [Theory]
    [InlineData("Home -1.5", MarketSelection.HandicapHome, -1.5)]
    [InlineData("Away +1.5", MarketSelection.HandicapAway, 1.5)]
    public void Handicap_labels_carry_a_signed_line(string label, MarketSelection side, double line)
    {
        var parsed = MarketSelectionParser.Parse(MarketKind.Handicap, label);

        Assert.Equal(side, parsed.Side);
        Assert.Equal(line, parsed.Line);
    }

    [Theory]
    [InlineData(MarketKind.TotalGoals, "3.5")]            // no over/under word
    [InlineData(MarketKind.TotalGoals, "Sub")]            // no line
    [InlineData(MarketKind.BothTeamsToScore, "Maybe")]
    [InlineData(MarketKind.NextGoal, "whoever")]
    [InlineData(MarketKind.Handicap, "-1.5")]             // no side
    public void An_unreadable_label_is_refused_rather_than_defaulted(MarketKind kind, string label)
        => Assert.Throws<ArgumentException>(() => MarketSelectionParser.Parse(kind, label));

    [Fact]
    public void Romanian_diacritics_are_folded_before_matching()
        => Assert.Equal(
            MarketSelection.NextGoalAway,
            MarketSelectionParser.Parse(MarketKind.NextGoal, "Oaspeți").Side);

    [Fact]
    public void Matching_is_on_whole_tokens_so_a_longer_word_does_not_fire_a_short_one()
    {
        // "sub" inside "substitution" must not read as Under, and "nu" inside "numar" must not read
        // as No. This is why the parser tokenises instead of calling Contains.
        Assert.Throws<ArgumentException>(
            () => MarketSelectionParser.Parse(MarketKind.BothTeamsToScore, "numar de goluri"));
    }
}
