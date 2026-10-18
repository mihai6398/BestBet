using BestBet.Data.Entities;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>What each way a bet can end pays back, on 10 staked at 2.50.</summary>
public class SettlementTests
{
    [Theory]
    [InlineData(BetStatus.Won, 25.00, 15.00)]
    [InlineData(BetStatus.Lost, 0.00, -10.00)]
    [InlineData(BetStatus.Void, 10.00, 0.00)]
    [InlineData(BetStatus.HalfWon, 17.50, 7.50)]
    [InlineData(BetStatus.HalfLost, 5.00, -5.00)]
    public void Each_result_pays_what_it_should(BetStatus status, double payout, double profit)
    {
        var (actualPayout, actualProfit) = Settlement.For(status, 10m, 2.50m);

        Assert.Equal((decimal)payout, actualPayout);
        Assert.Equal((decimal)profit, actualProfit);
    }

    [Fact]
    public void A_cash_out_pays_what_the_bookmaker_paid()
        => Assert.Equal((12.30m, 2.30m), Settlement.For(BetStatus.CashedOut, 10m, 2.50m, 12.30m));

    [Fact]
    public void Payout_and_profit_are_rounded_once_and_always_agree()
    {
        // 3.33 at 1.91 is 6.3603: the payout rounds to 6.36 and the profit is exactly that less the stake.
        var (payout, profit) = Settlement.For(BetStatus.Won, 3.33m, 1.91m);

        Assert.Equal(6.36m, payout);
        Assert.Equal(payout - 3.33m, profit);
    }

    [Fact]
    public void A_cash_out_needs_its_amount_and_an_open_bet_has_nothing_to_settle()
    {
        Assert.Throws<AnalysisInputException>(() => Settlement.For(BetStatus.CashedOut, 10m, 2.50m));
        Assert.Throws<AnalysisInputException>(() => Settlement.For(BetStatus.CashedOut, 10m, 2.50m, -1m));
        Assert.Throws<AnalysisInputException>(() => Settlement.For(BetStatus.Open, 10m, 2.50m));
    }
}
