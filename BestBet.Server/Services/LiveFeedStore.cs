using System.Collections.Concurrent;
using BestBet.Contracts;

namespace BestBet.Services;

/// <summary>
/// The last reading pushed in from a live-score page, one per user.
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
    /// <summary>A whole match page is tens of kilobytes; this is generous and still bounded.</summary>
    public const int MaxTextLength = 200_000;

    private readonly ConcurrentDictionary<Guid, Feed> _byUser = new();
    private readonly ConcurrentDictionary<string, Guid> _byToken = new(StringComparer.Ordinal);

    /// <summary>
    /// The user's secret for the push endpoint, made the first time they ask and kept for the run.
    /// </summary>
    /// <remarks>
    /// The push endpoint has to accept a cross-origin POST from a page this app does not control, and
    /// it cannot carry the login cookie — a SameSite=Strict cookie is never sent from another site. So
    /// the token is both the gate and the only thing that says whose reading this is. It is only ever
    /// readable from this app's own origin by the signed-in user, which means a page that was not
    /// handed it cannot push anything, and one user's bookmarklet can only ever feed that user.
    /// </remarks>
    public string TokenFor(Guid userId) => FeedFor(userId).Token;

    public LiveFeedSnapshot? Latest(Guid userId)
        => _byUser.TryGetValue(userId, out var feed) ? Volatile.Read(ref feed.Latest) : null;

    /// <summary>Stores a reading for whoever owns the token. False when the token is nobody's.</summary>
    public bool Publish(string token, string text, string? source)
    {
        if (!_byToken.TryGetValue(token, out var userId) || !_byUser.TryGetValue(userId, out var feed))
        {
            return false;
        }

        var trimmed = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        Volatile.Write(ref feed.Latest, new LiveFeedSnapshot(trimmed, DateTime.UtcNow, source));
        return true;
    }

    public void Clear(Guid userId)
    {
        if (_byUser.TryGetValue(userId, out var feed))
        {
            Volatile.Write(ref feed.Latest, null);
        }
    }

    private Feed FeedFor(Guid userId) => _byUser.GetOrAdd(userId, id =>
    {
        var feed = new Feed(Guid.NewGuid().ToString("N"));
        _byToken[feed.Token] = id;
        return feed;
    });

    private sealed class Feed(string token)
    {
        public string Token { get; } = token;

        public LiveFeedSnapshot? Latest;
    }
}
