using BestBet.Contracts;

namespace BestBet.Services;

/// <summary>
/// The last reading pushed in from a live-score page the user has open.
///
/// Why a push and not a poll: Sofascore answers a programmatic request with 403 — every request,
/// including robots.txt — because Cloudflare rejects anything that is not a real browser. Making a
/// server-side poller work would mean impersonating one, which is both against their terms and the
/// kind of thing that breaks silently and permanently. The browser the user already has the match
/// open in is allowed, so that is where the reading comes from; this just holds the last one.
///
/// The text arrives raw and is parsed in the browser by the same parser a manual paste goes through.
/// That is deliberate: no second implementation to drift, and a redesign on their side breaks one
/// thing instead of two.
/// </summary>
public class LiveFeedStore
{
    /// <summary>
    /// Shared secret for the push endpoint, made once per run.
    ///
    /// The endpoint has to accept a cross-origin POST from a page this app does not control, and a
    /// browser will send a simple POST whatever the CORS policy says — the policy only decides who
    /// may read the reply. So the token is the actual gate. It is only ever readable from this app's
    /// own origin, which means a page that was not handed it cannot push anything.
    /// </summary>
    public string Token { get; } = Guid.NewGuid().ToString("N");

    /// <summary>A whole match page is tens of kilobytes; this is generous and still bounded.</summary>
    public const int MaxTextLength = 200_000;

    private LiveFeedSnapshot? _latest;

    public LiveFeedSnapshot? Latest => Volatile.Read(ref _latest);

    public void Publish(string text, string? source)
    {
        var trimmed = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        Volatile.Write(ref _latest, new LiveFeedSnapshot(trimmed, DateTime.UtcNow, source));
    }

    public void Clear() => Volatile.Write(ref _latest, null);
}
