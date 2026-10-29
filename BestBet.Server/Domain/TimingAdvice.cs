namespace BestBet.Domain;

/// <summary>What the timing plan recommends doing with a live price.</summary>
public enum TimingAction
{
    /// <summary>Taking the price now is worth at least as much as any later moment the plan looked at.</summary>
    BetNow,

    /// <summary>A later moment is worth more, or something in the live data says the number is not ready yet.</summary>
    Wait,

    /// <summary>Neither now nor in the next quarter of an hour is there value the plan can trust.</summary>
    Skip,
}

/// <summary>
/// One warning about the live data, as a code the client words in the reader's language and the
/// numbers it needs.
/// </summary>
/// <param name="Code">One of the codes on <c>TimingAdvisor</c>, e.g. <c>edge-too-large</c>.</param>
/// <param name="Value">The number the reason is about: the edge in points, the age in seconds, a rate
/// per ninety minutes, a share of the estimate. Which one is fixed by the code.</param>
/// <param name="Reference">What that number is being compared with, when the reason compares two.</param>
public record TimingReason(string Code, double? Value = null, double? Reference = null);

/// <summary>
/// One kind of thing that can happen before a later moment, and what the bet looks like then.
/// </summary>
/// <param name="Code"><c>no-goal</c>, <c>home-goal</c>, <c>away-goal</c> or <c>more-goals</c>.</param>
/// <param name="Probability">How likely it is, weighing the model's view and the market's by how far
/// each is believed now.</param>
/// <param name="ModelProbability">The model's chance for the bet if it happens.</param>
/// <param name="MarketProbability">The market's fair chance for the bet if it happens.</param>
/// <param name="Odds">The price the bookmaker would most likely be showing. Null when the bet would be
/// settled or priced too short to be worth taking by then.</param>
/// <param name="BetShare">How much of this scenario ends in a bet worth taking, 0..1.</param>
/// <param name="Credibility">How far the model is believed over the market by then, once the minutes
/// and the goals in between have been counted.</param>
public record TimingScenario(
    string Code,
    double Probability,
    double ModelProbability,
    double MarketProbability,
    double? Odds,
    double BetShare,
    double Credibility);

/// <summary>One moment the bet could be taken at — now, or a few minutes of play from now.</summary>
/// <param name="MinutesFromNow">Minutes of play from now; 0 is now.</param>
/// <param name="AtMinute">The minute on the clock by then.</param>
/// <param name="ExpectedValue">Expected profit of the plan "wait until then, bet only if it is still
/// worth it", as a share of the bankroll. Stake and edge both count: a better price on a smaller stake
/// can be worth less.</param>
/// <param name="BetProbability">The chance the bet is still worth taking by then.</param>
/// <param name="ExpectedOdds">The price it would most likely be taken at, when it is.</param>
/// <param name="StakeFraction">The stake it would most likely be taken with, as a share of the bankroll.</param>
/// <param name="Scenarios">What can happen in between, grouped. Empty for now.</param>
public record TimingOption(
    int MinutesFromNow,
    int AtMinute,
    double ExpectedValue,
    double BetProbability,
    double? ExpectedOdds,
    double StakeFraction,
    IReadOnlyList<TimingScenario> Scenarios);

/// <summary>
/// The timing plan for a live bet: whether to take the price now or later, and the working behind it.
/// </summary>
/// <param name="Action">The recommendation.</param>
/// <param name="WaitMinutes">How many minutes of play to wait, when waiting has a moment to wait for.</param>
/// <param name="Reasons">Warnings about the live data, most serious first. Any of them turns a
/// "bet now" into a wait.</param>
/// <param name="Credibility">How far the model is believed over the market right now, 0..1: the share
/// of its estimate built from this match rather than from a prior the market knows better.</param>
/// <param name="ModelRemainingGoals">Goals the model expects in the rest of the match, both sides.</param>
/// <param name="MarketRemainingGoals">The same, read back out of the market's fair price. Null for a
/// market it cannot be read out of, or a line that is already settled.</param>
/// <param name="LeaguePrior">True when the pace was pulled towards a league-average match because no
/// pre-match numbers were given — the most common reason the model and the market disagree.</param>
/// <param name="Options">Now first, then each later moment that leaves enough match to bet on.</param>
public record TimingAdvice(
    TimingAction Action,
    int? WaitMinutes,
    IReadOnlyList<TimingReason> Reasons,
    double Credibility,
    double ModelRemainingGoals,
    double? MarketRemainingGoals,
    bool LeaguePrior,
    IReadOnlyList<TimingOption> Options);
