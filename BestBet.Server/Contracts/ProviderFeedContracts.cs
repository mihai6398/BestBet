namespace BestBet.Contracts;

/// <summary>
/// One extra stat, in the shape the client's own paste parser produces.
/// </summary>
/// <remarks>
/// The values are strings rather than numbers on purpose: this mirrors <c>StatRow</c> in
/// <c>bestbet.client/src/lib/statsPaste.ts</c>, where a stat is whatever the provider printed —
/// "56", "1.34", "68%" — and only the fields the model actually needs are parsed into numbers.
/// Keeping the two shapes identical is what lets a fetched reading and a pasted one go down the
/// same path in the UI instead of each having their own.
/// </remarks>
/// <param name="Id">The client's own stat key, e.g. <c>shotsOnTarget</c>. Names the row in the user's language.</param>
/// <param name="Label">English name, the fallback for a stat the dictionary has not been taught.</param>
public record ProviderStatRow(string Id, string Label, string Home, string Away);

/// <summary>
/// A reading fetched from a provider that answers us, rather than pushed in from the user's browser.
/// </summary>
/// <remarks>
/// Deliberately the same fields as the client's <c>ParsedStats</c>. The bookmarklet route sends raw
/// page text and the browser parses it; this route has the server do the equivalent work against a
/// JSON API. Everything downstream — the form, the re-pricing, the staleness warning — then works
/// on one shape and cannot tell the two routes apart, which is the point: there is no second set of
/// conventions to keep in agreement about what "minute 63" means.
/// </remarks>
/// <param name="Provider">Which site answered, so the panel can say where the numbers came from.</param>
/// <param name="MatchId">The provider's own id, which is also what makes the next poll cheap.</param>
/// <param name="Url">A link back to the match page, so the user can check this rather than trust it.</param>
/// <param name="MatchLabel">"Home - Away", as the provider spells the names.</param>
/// <param name="Minute">The minute in progress, derived the same way the paste parser derives it.</param>
/// <param name="XgHome">Cumulative xG. Null for a league this provider does not compute it for.</param>
/// <param name="Extras">Everything else recognised, so the user can see the right match was found.</param>
/// <param name="Missing">
/// The fields an analysis needs and this provider did not supply, as the client's own keys
/// (<c>minute</c>, <c>xgHome</c>, <c>xgAway</c>). A non-empty list means this match cannot be
/// watched here and the bookmarklet is the only route left — which the UI has to say plainly,
/// because a reading that arrives looking complete and is not would size a stake on a guess.
/// </param>
/// <param name="Ongoing">Whether the provider considers the match in progress right now.</param>
/// <param name="Finished">Whether it is over, which is when watching it stops being useful.</param>
/// <param name="ReadUtc">When the provider was actually asked — not when this response was sent.</param>
/// <param name="AgeSeconds">
/// How old the reading already was when it left here, which is non-zero whenever it was served from
/// the short cache in front of the provider. Mirrors <see cref="LiveFeedState.AgeSeconds"/>: the
/// browser adds its own elapsed time to this, so staleness is measured without either side having
/// to trust the other's clock.
/// </param>
public record ProviderReading(
    string Provider,
    string MatchId,
    string? Url,
    string? MatchLabel,
    int? Minute,
    int? GoalsHome,
    int? GoalsAway,
    double? XgHome,
    double? XgAway,
    IReadOnlyList<ProviderStatRow> Extras,
    IReadOnlyList<string> Missing,
    bool Ongoing,
    bool Finished,
    DateTime ReadUtc,
    double AgeSeconds = 0.0);
