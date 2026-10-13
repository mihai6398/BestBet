using BestBet.Services;

namespace BestBet.Server.Tests.Services;

public class OddsMathTests
{
    [Theory]
    [InlineData(1.57, 0.6369426751592356)]
    [InlineData(1.66, 0.6024096385542169)]
    [InlineData(2.00, 0.5)]
    [InlineData(1.60, 0.625)]
    public void ImpliedProbability_is_the_reciprocal_of_the_price(double odds, double expected)
        => Assert.Equal(expected, OddsMath.ImpliedProbability(odds), 12);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.9)]
    [InlineData(-2.0)]
    public void ImpliedProbability_rejects_prices_that_cannot_exist(double odds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => OddsMath.ImpliedProbability(odds));

    /// <summary>
    /// The worked example from the spec: a two-way book priced 1.57 / 1.66 carries a 23.9% overround
    /// and de-vigs to 51.4% on the 1.57 side.
    /// </summary>
    [Fact]
    public void DeVig_reproduces_the_two_way_worked_example()
    {
        var overround = OddsMath.Overround(1.57, [1.66]);
        Assert.Equal(1.2394, overround, 4);

        var fair = OddsMath.DeVig(1.57, [1.66]);
        Assert.Equal(0.5139, fair, 4);
    }

    [Fact]
    public void DeVig_across_a_whole_market_sums_to_exactly_one()
    {
        double[] book = [2.10, 3.40, 3.80];

        var home = OddsMath.DeVig(book[0], [book[1], book[2]]);
        var draw = OddsMath.DeVig(book[1], [book[0], book[2]]);
        var away = OddsMath.DeVig(book[2], [book[0], book[1]]);

        Assert.Equal(1.0, home + draw + away, 12);
    }

    [Fact]
    public void DeVig_of_a_margin_free_book_leaves_the_price_untouched()
    {
        // 2.00 / 2.00 is already fair, so removing the vig has nothing to remove.
        Assert.Equal(0.5, OddsMath.DeVig(2.00, [2.00]), 12);
    }

    [Fact]
    public void DeVig_always_lowers_the_probability_a_real_book_implies()
    {
        // A book with margin overstates every outcome; de-vigging has to bring each one down.
        Assert.True(OddsMath.DeVig(1.57, [1.66]) < OddsMath.ImpliedProbability(1.57));
        Assert.True(OddsMath.DeVig(1.66, [1.57]) < OddsMath.ImpliedProbability(1.66));
    }

    [Fact]
    public void DeVig_refuses_to_guess_when_the_other_side_is_missing()
    {
        // One price on its own says nothing about the margin baked into it, and silently assuming
        // zero margin would flatter every single analysis.
        var ex = Assert.Throws<ArgumentException>(() => OddsMath.DeVig(1.57, []));
        Assert.Contains("sibling", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.60, 1.57, -0.058)]
    [InlineData(0.5459030764858998, 1.60, -0.1266)]
    [InlineData(0.70, 1.60, 0.12)]
    public void ExpectedValuePerUnit_prices_the_bet_at_the_odds_actually_offered(
        double ownProbability,
        double odds,
        double expected)
        => Assert.Equal(expected, OddsMath.ExpectedValuePerUnit(ownProbability, odds), 3);
}
