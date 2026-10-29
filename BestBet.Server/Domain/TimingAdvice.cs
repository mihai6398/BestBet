namespace BestBet.Domain;

/// <summary>Whether to take the price now or come back to it.</summary>
public enum TimingAction
{
    /// <summary>Nothing in the live data says the number is about to be wrong.</summary>
    BetNow,

    /// <summary>At least one thing does; the reasons say what, and what would change it.</summary>
    Wait,
}

/// <summary>
/// One reason to wait, as a code the client words in the reader's language and the numbers it needs.
/// </summary>
/// <param name="Code">One of the codes on <c>TimingAdvisor</c>, e.g. <c>edge-too-large</c>.</param>
/// <param name="Value">The number the reason is about: the edge in points, the age in seconds, a rate
/// per ninety minutes, a share of the estimate. Which one is fixed by the code.</param>
/// <param name="Reference">What that number is being compared with, when the reason compares two.</param>
public record TimingReason(string Code, double? Value = null, double? Reference = null);

/// <summary>
/// Whether this is the moment to take a live price, and why not when it is not.
/// </summary>
/// <param name="Action">The recommendation.</param>
/// <param name="Reasons">Every reason to wait that fired, most serious first. Empty for <see cref="TimingAction.BetNow"/>.</param>
/// <param name="ModelRemainingGoals">Goals the model expects in the rest of the match, both sides.</param>
/// <param name="MarketRemainingGoals">The same, read back out of the market's fair price: the number
/// of goals that would make the model agree with the market. Null for a market it cannot be read out
/// of, or a line that is already settled.</param>
/// <param name="LeaguePrior">True when the pace was pulled towards a league-average match because no
/// pre-match numbers were given — the most common reason the model and the market disagree.</param>
public record TimingAdvice(
    TimingAction Action,
    IReadOnlyList<TimingReason> Reasons,
    double ModelRemainingGoals,
    double? MarketRemainingGoals,
    bool LeaguePrior);
