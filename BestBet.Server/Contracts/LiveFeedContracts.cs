namespace BestBet.Contracts;

/// <summary>One reading pushed in from a live-score page.</summary>
/// <param name="Text">The page as text, exactly as the browser read it. Parsed client-side.</param>
/// <param name="ReceivedUtc">When it arrived here.</param>
/// <param name="Source">The page it came from, so the user can see which match is being watched.</param>
public record LiveFeedSnapshot(string Text, DateTime ReceivedUtc, string? Source);

/// <summary>
/// Body of <c>POST /api/live-feed</c>.
/// </summary>
/// <remarks>
/// Sent as <c>text/plain</c> on purpose. A JSON content type makes the browser send a CORS preflight
/// first, and the page pushing this is one we do not control and cannot debug from; a simple request
/// has fewer ways to fail quietly on someone else's site.
/// </remarks>
public record LiveFeedPush(string Token, string Text, string? Source);

/// <summary>
/// Response of <c>GET /api/live-feed</c>: what is being watched, and how stale it is.
/// </summary>
/// <param name="Token">The push secret, readable only from this app's own origin.</param>
/// <param name="Text">The last reading, or null when nothing has been pushed yet.</param>
/// <param name="ReceivedUtc">When it arrived, or null.</param>
/// <param name="AgeSeconds">How old the reading is. The number that says whether to trust it.</param>
/// <param name="Source">The page it came from.</param>
public record LiveFeedState(
    string Token,
    string? Text,
    DateTime? ReceivedUtc,
    double? AgeSeconds,
    string? Source);
