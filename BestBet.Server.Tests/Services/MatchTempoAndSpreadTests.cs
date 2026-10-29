using BestBet.Domain;
using BestBet.Services;

namespace BestBet.Server.Tests.Services;

/// <summary>
/// The two corrections that came out of live Unders losing to goal-fests: the tempo of a match, and
/// the spread around a rate the model can only estimate.
/// </summary>
public class MatchTempoAndSpreadTests
{
    private const double Match = MatchClock.TypicalMatchMinutes;

    // --- Tempo ----------------------------------------------------------------

    [Fact]
    public void A_whole_match_is_worth_exactly_a_match()
    {
        // The line has to average one, or a per-match prior run through it would come out changed.
        Assert.Equal(Match, MatchTempo.Cumulative(Match, Match), 10);
    }

    [Fact]
    public void Fewer_goals_come_before_half_time_than_after()
    {
        var firstHalf = MatchTempo.Cumulative(MatchClock.HalfMinutes + MatchClock.ExpectedFirstHalfStoppage, Match) / Match;

        Assert.Equal(0.455, firstHalf, 3);
    }

    [Fact]
    public void The_opening_is_quieter_than_an_average_minute_and_the_end_livelier()
    {
        Assert.True(MatchTempo.Intensity(0, 20, Match) < 0.9);
        Assert.True(MatchTempo.Intensity(75, Match, Match) > 1.1);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(30.0)]
    [InlineData(70.0)]
    [InlineData(105.0)]
    public void MinuteAt_undoes_Cumulative(double minute)
        => Assert.Equal(minute, MatchTempo.MinuteAt(MatchTempo.Cumulative(minute, Match), Match), 9);

    [Fact]
    public void Counting_the_tempo_raises_an_early_rate()
    {
        // The user's own case: 0-0 at minute 23 on half a goal of xG, an Under 3.5 on the screen.
        var state = new LiveMatchState(23, 0.30, 0.20, 0, 0, false);
        var flat = PaceModel.Adjust(state, new PaceSettings(TimeProfile: false, RateUncertainty: false));
        var tempo = PaceModel.Adjust(state, new PaceSettings(RateUncertainty: false));

        Assert.True(tempo.AdjustedRateTotal > flat.AdjustedRateTotal * 1.05);
        Assert.Contains(tempo.Steps, s => s.Contains("Tempo counted"));
    }

    // --- Spread ---------------------------------------------------------------

    [Fact]
    public void The_negative_binomial_keeps_the_mean_and_widens_the_spread()
    {
        const double mean = 2.0;
        const double shape = 10.0;
        var table = PoissonGoalModel.NegativeBinomialPmfTable(mean, shape, 60);

        var total = table.Sum();
        var expected = table.Select((p, k) => p * k).Sum();
        var variance = table.Select((p, k) => p * (k - expected) * (k - expected)).Sum();

        Assert.Equal(1.0, total, 10);
        Assert.Equal(mean, expected, 8);
        Assert.Equal(mean + mean * mean / shape, variance, 8);
        Assert.True(table[0] > PoissonGoalModel.PoissonPmf(0, mean));
    }

    [Fact]
    public void A_spread_rate_makes_an_under_less_safe_than_a_known_one()
    {
        var state = new LiveMatchState(23, 0.30, 0.20, 0, 0, false);
        var settings = new PaceSettings(RateUncertainty: false);
        var spread = new PaceSettings();
        var selection = new MarketSelectionSpec(MarketSelection.Under, 3.5);

        var known = MarketProbabilityResolver.Resolve(
            MarketKind.TotalGoals, selection, PoissonGoalModel.ExtrapolateRemainingGoals(state, settings));
        var estimated = MarketProbabilityResolver.Resolve(
            MarketKind.TotalGoals, selection, PoissonGoalModel.ExtrapolateRemainingGoals(state, spread));

        Assert.True(estimated.Probability < known.Probability);
    }

    [Fact]
    public void No_further_goal_follows_the_spread_too()
    {
        // Read off the tables rather than as e^-λ, or the two markets would disagree about zero.
        var state = new LiveMatchState(70, 0.9, 0.7, 1, 1, false);
        var distribution = PoissonGoalModel.ExtrapolateRemainingGoals(state, PaceSettings.Default);

        var none = MarketProbabilityResolver.Resolve(
            MarketKind.NextGoal, new MarketSelectionSpec(MarketSelection.NextGoalNone, null), distribution);

        Assert.Equal(distribution.RemainingTotalPmf()[0], none.Probability, 10);
    }

    [Fact]
    public void Flat_settings_still_mean_a_plain_poisson()
    {
        var pace = PaceModel.Adjust(new LiveMatchState(60, 1.0, 1.0, 0, 0, false), PaceSettings.Flat);

        Assert.Null(pace.RateShape);
        Assert.True(PaceSettings.Flat.IsFlat);
        Assert.False(PaceSettings.Default.IsFlat);
    }
}
