using BestBet.Contracts;
using BestBet.Domain;

namespace BestBet.Services;

/// <summary>
/// A provider's reading, turned into the match state and pace settings the model runs on.
/// </summary>
/// <remarks>
/// The browser does the same for the match it is watching (see <c>feedLiveState</c> in
/// <c>App.tsx</c>); this is the server's half, for a bet whose match the server fetched itself. The
/// two have to agree on what each field means, which is why this does nothing but carry fields
/// across and leaves every judgement to the model.
/// </remarks>
public static class ProviderLiveState
{
    /// <summary>Why a reading cannot be priced, as a key the client words for itself.</summary>
    public const string NotStarted = "not-started";

    public const string NoXg = "no-xg";

    public const string Finished = "finished";

    public const string NotFound = "not-found";

    /// <summary>The state to price, or null with the reason it cannot be.</summary>
    /// <remarks>
    /// A finished match is not priced. Its clock reads 90 with no period, which the model would take
    /// for the 90th minute with stoppage still to come — a confident number about a match that is
    /// over, when the score has already settled the bet one way or the other.
    /// </remarks>
    public static (LiveMatchState? State, string? Unavailable) From(ProviderReading reading)
    {
        // In this order: before kick-off there is no xG either, and that is not the league's fault.
        if (reading.Finished) return (null, Finished);
        if (reading.Minute is null) return (null, NotStarted);
        if (reading.XgHome is null || reading.XgAway is null) return (null, NoXg);

        return (new LiveMatchState(
            reading.Minute.Value,
            reading.XgHome.Value,
            reading.XgAway.Value,
            reading.GoalsHome ?? 0,
            reading.GoalsAway ?? 0,
            IsPreMatch: false,
            reading.Period,
            reading.AddedTimeFirstHalf,
            reading.AddedTimeSecondHalf,
            reading.RedCardsHome,
            reading.RedCardsAway,
            reading.LastRedCardMinute), null);
    }

    /// <summary>
    /// The caller's choice of corrections, with the recent window taken from this reading.
    /// </summary>
    /// <remarks>
    /// A window the caller sends belongs to whatever moment of whichever match it was typed for, so
    /// it is replaced rather than merged. The spec's flat model is left exactly as asked: it has no
    /// recency, and adding one would make "corrections off" not mean off.
    /// </remarks>
    public static PaceSettings Pace(PaceSettings? requested, ProviderReading reading)
    {
        var pace = requested ?? PaceSettings.Default;

        if (pace is { Shrink: false, ScoreState: false, Stoppage: false, RedCards: false })
        {
            return pace;
        }

        return pace with
        {
            RecentWindowMinutes = reading.RecentWindowMinutes,
            RecentXgHome = reading.RecentXgHome,
            RecentXgAway = reading.RecentXgAway,
        };
    }
}
