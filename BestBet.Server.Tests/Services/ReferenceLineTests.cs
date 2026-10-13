using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// A second book's prices, used to decide whether there is an edge — while the stake stays sized
/// against the price actually on offer.
/// </summary>
public class ReferenceLineTests
{
    private readonly AnalysisService _analysis = new();

    /// <summary>A wide bookmaker: 1.57 / 1.66 is a 24% overround.</summary>
    private static MarketOdds WideBook() => new(MarketKind.TotalGoals, "Sub 3.5", 1.57, [1.66]);

    /// <summary>A prediction market on the same outcome: 60/40, so a margin of effectively nothing.</summary>
    private static MarketOdds PredictionMarket() => new(MarketKind.TotalGoals, "Under 3.5", 1.0 / 0.60, [1.0 / 0.40]);

    private static AnalyzeRequest Request(MarketOdds? reference = null, string? source = null) => new(
        Market: WideBook(),
        LiveState: null,
        Bankroll: 100.0,
        Selection: MarketSelection.Under,
        Line: 3.5,
        ManualOwnProbability: 0.65,
        ReferenceMarket: reference,
        ReferenceSource: source);

    [Fact]
    public void Without_a_reference_nothing_changes()
    {
        var result = _analysis.Analyze(Request());

        Assert.Null(result.Reference);
        Assert.Equal(OddsMath.DeVig(1.57, [1.66]), result.Edge.FairMarketProbability, 10);
    }

    [Fact]
    public void The_edge_is_measured_against_the_reference_not_against_the_bookmaker()
    {
        // The bookmaker's own de-vig says 51.4%; the prediction market says 60%. An estimate of 65%
        // is a 13.6 point edge over the wide book and only a 5 point edge over the sharp one — and
        // the sharp one is the one more likely to be right.
        var result = _analysis.Analyze(Request(PredictionMarket(), "Polymarket"));

        Assert.NotNull(result.Reference);
        Assert.Equal(0.60, result.Edge.FairMarketProbability, 10);
        Assert.Equal(5.0, result.Edge.EdgePercentagePoints, 10);
    }

    [Fact]
    public void The_price_you_can_get_still_decides_EV_and_the_stake()
    {
        // This is the whole point of keeping the two apart. EV has to be computed at 1.57, because
        // 1.57 is what will be paid; sizing a stake against a price on an exchange you are not
        // betting into would be the one way this feature loses money.
        var withReference = _analysis.Analyze(Request(PredictionMarket(), "Polymarket"));
        var without = _analysis.Analyze(Request());

        Assert.Equal(OddsMath.ExpectedValuePerUnit(0.65, 1.57), withReference.Edge.ExpectedValuePerUnit, 10);
        Assert.Equal(without.Edge.ExpectedValuePerUnit, withReference.Edge.ExpectedValuePerUnit, 10);
    }

    [Fact]
    public void Both_fair_lines_come_back_so_the_disagreement_is_visible()
    {
        var result = _analysis.Analyze(Request(PredictionMarket(), "Polymarket"));
        var reference = result.Reference!;

        Assert.Equal("Polymarket", reference.Source);
        Assert.Equal(OddsMath.DeVig(1.57, [1.66]), reference.BookFairProbability, 10);
        Assert.Equal(0.60, reference.FairProbability, 10);
        Assert.Equal((0.60 - reference.BookFairProbability) * 100.0, reference.DisagreementPoints, 10);
    }

    [Fact]
    public void A_prediction_market_reports_an_overround_of_about_one()
    {
        // 1.00 is the signature of a market with no margin to remove, and it is why its de-vig is
        // worth more than a bookmaker's. Showing it is how the user can tell a real one from a
        // mislabelled bookmaker feed.
        var result = _analysis.Analyze(Request(PredictionMarket(), "Polymarket"));

        Assert.Equal(1.0, result.Reference!.Overround, 10);
        Assert.True(result.Overround > 1.2);
    }

    [Fact]
    public void The_source_gets_a_name_even_when_none_was_given()
        => Assert.Equal("reference book", _analysis.Analyze(Request(PredictionMarket())).Reference!.Source);

    [Fact]
    public void A_reference_with_no_siblings_is_refused_for_the_same_reason_a_book_is()
    {
        var oneSided = new MarketOdds(MarketKind.TotalGoals, "Under 3.5", 1.67, []);

        var error = Assert.Throws<AnalysisInputException>(() => _analysis.Analyze(Request(oneSided)));
        Assert.Contains("other outcomes", error.Message);
    }

    [Fact]
    public void A_reference_price_at_or_below_evens_is_refused()
    {
        var impossible = new MarketOdds(MarketKind.TotalGoals, "Under 3.5", 1.0, [2.0]);

        Assert.Throws<AnalysisInputException>(() => _analysis.Analyze(Request(impossible)));
    }

    [Fact]
    public void A_sharper_line_can_take_a_bet_off_the_table()
    {
        // The case that makes this worth building. A 62% estimate against a book that de-vigs to
        // 51.4% looks like a ten-point edge and clears the pre-match threshold comfortably. Against
        // a market with no margin saying 60%, the same estimate is two points — inside the marginal
        // band, and not a bet. The difference was never in the estimate; it was in the yardstick.
        var estimate = Request() with { ManualOwnProbability = 0.62 };

        var againstWideBook = _analysis.Analyze(estimate);
        var againstSharpLine = _analysis.Analyze(estimate with
        {
            ReferenceMarket = PredictionMarket(),
            ReferenceSource = "Polymarket",
        });

        Assert.Equal(Verdict.Bet, againstWideBook.Verdict);
        Assert.Equal(Verdict.Marginal, againstSharpLine.Verdict);
        Assert.Equal(2.0, againstSharpLine.Edge.EdgePercentagePoints, 10);
    }
}
