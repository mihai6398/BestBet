using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;
using Xunit.Abstractions;

namespace BestBet.Server.Tests;

public class ScratchProbe(ITestOutputHelper output)
{
    private readonly AnalysisService _service = new();

    private void Show(string name, AnalyzeRequest request)
    {
        foreach (var (label, pace) in new[]
                 {
                     ("old", (PaceSettings?)new PaceSettings(TimeProfile: false, RateUncertainty: false)),
                     ("tempo", new PaceSettings(RateUncertainty: false)),
                     ("new", null),
                 })
        {
            var r = _service.Analyze(request with { Pace = pace ?? request.Pace });
            output.WriteLine(
                $"{name,-28} {label,-6} own {r.Edge.OwnProbability:0.0000} fair {r.Edge.FairMarketProbability:0.000} " +
                $"edge {r.Edge.EdgePercentagePoints,6:0.0} {r.Verdict,-8} goalsLeft {r.Model!.ExpectedRemainingHome + r.Model.ExpectedRemainingAway:0.00} " +
                $"timing {r.Timing?.Action} [{string.Join(",", r.Timing?.Reasons.Select(x => $"{x.Code}:{x.Value:0.##}") ?? [])}] mkt {r.Timing?.MarketRemainingGoals:0.00}");
        }
    }

    [Fact]
    public void Probe()
    {
        Show("Barca-Racing Over6.5 36'", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 6.5", 1.91, [1.91]),
            new LiveMatchState(36, 2.38, 0.30, 3, 1, false), 263, MarketSelection.Over, 6.5));

        Show("Example Under3.5 23' 0-0", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 3.5", 1.22, [4.20]),
            new LiveMatchState(23, 0.30, 0.20, 0, 0, false), 263, MarketSelection.Under, 3.5));

        Show("Bet1 PSG-Barca U4 38'", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.77, [2.20]),
            new LiveMatchState(38, 1.43, 0.38, 1, 0, false), 263, MarketSelection.Under, 4));

        Show("Bet2 PSG-Barca U3 22'", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 3", 2.38, [1.47]),
            new LiveMatchState(22, 0.08, 0.07, 1, 0, false), 263, MarketSelection.Under, 3));

        Show("Bet3 Juve-Rennes U5 48'", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 5", 1.47, [2.45]),
            new LiveMatchState(48, 0.58, 1.27, 1, 2, false), 263, MarketSelection.Under, 5));

        Show("Bet4 Ipswich U4 15'", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 4", 1.28, [3.40]),
            new LiveMatchState(15, 0.06, 0.50, 0, 1, false), 263, MarketSelection.Under, 4));

        Show("Late Over0.5 76' 0-0", new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 0.5", 1.75, [2.10]),
            new LiveMatchState(76, 0.35, 0.20, 0, 0, false), 263, MarketSelection.Over, 0.5));
    }
}
