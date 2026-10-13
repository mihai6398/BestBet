namespace BestBet.Domain;

/// <summary>
/// How the observed xG rate is turned into a forward-looking one.
/// </summary>
/// <remarks>
/// The spec's model reads a flat average off the clock — total xG divided by minutes played — and runs
/// it to full time. That is badly wrong at both ends of a match's rhythm. A side sitting on 2.38 xG
/// after 36 minutes gets projected for 5.95 over ninety, a total that almost never happens; a side
/// that has been quiet gets projected to stay quiet. These settings are the correction, and every one
/// of them can be switched off to get the spec's original behaviour back.
/// </remarks>
/// <param name="Shrink">Pull the observed rate towards a league-average prior. This is the big one:
/// a rate read off 36 minutes is a small-sample estimate, and treating it as fact is what produces
/// the impossible projections.</param>
/// <param name="ScoreState">Account for the fact that a team protecting a lead attacks less and a
/// team chasing the game attacks more.</param>
/// <param name="RecentWindowMinutes">Length of a recent window the caller has xG for — 45 for "this
/// half", 15 for "the last quarter of an hour". Null means no recent data was supplied.</param>
/// <param name="RecentXgHome">Home xG accrued inside that window.</param>
/// <param name="RecentXgAway">Away xG accrued inside that window.</param>
/// <param name="RecencyWeight">0..1. How much of the blended rate comes from the recent window rather
/// than the whole-match average. 0.5 splits it evenly.</param>
public record PaceSettings(
    bool Shrink = true,
    bool ScoreState = true,
    double? RecentWindowMinutes = null,
    double? RecentXgHome = null,
    double? RecentXgAway = null,
    double RecencyWeight = 0.5)
{
    /// <summary>The spec's original behaviour: the raw rate, extrapolated flat.</summary>
    public static readonly PaceSettings Flat = new(Shrink: false, ScoreState: false);

    /// <summary>Everything on, with no recent window supplied.</summary>
    public static readonly PaceSettings Default = new();

    /// <summary>True when the caller gave a complete recent window to blend in.</summary>
    public bool HasRecentWindow =>
        RecentWindowMinutes is > 0 && RecentXgHome is not null && RecentXgAway is not null;
}
