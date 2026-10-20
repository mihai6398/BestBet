using System.Text.Json;
using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

/// <summary>
/// The live feed, by either of the two routes a reading can arrive on.
///
/// The original one is a push: a bookmarklet on the match page the user has open sends the page as
/// text, because Sofascore answers a programmatic request with 403 and there is no honest way round
/// that. The second is a pull — <c>GET provider</c> below — for FotMob, which answers us directly,
/// so a pasted link is enough and no tab has to stay open.
///
/// Both live on this controller because they answer the same question and the client treats them as
/// one channel with a precedence rule. See <see cref="FotMobClient"/> for why the push route is
/// still the only one for some leagues.
/// </summary>
[ApiController]
[Route("api/live-feed")]
public class LiveFeedController(LiveFeedStore feed, FotMobClient fotmob, ICurrentUser user) : ControllerBase
{
    /// <summary>
    /// Reads a match from a provider that answers us, given a link or an id the user pasted.
    /// </summary>
    /// <remarks>
    /// A GET because it changes nothing: each call is one reading of a match in progress, and the
    /// browser repeats it on a timer. Nothing is stored — unlike the push route, there is no state
    /// here to go stale, because the next call simply asks again.
    ///
    /// Not cross-origin, and it must not become so. The push endpoint has to accept a POST from a
    /// provider's own page and is guarded by a token for exactly that reason; this one is only ever
    /// called by this app's own UI, and leaving it same-origin keeps it from becoming a way for any
    /// page the user visits to make this machine fetch arbitrary matches.
    /// </remarks>
    /// <param name="match">A FotMob match link or id, or a Sofascore match link.</param>
    [HttpGet("provider")]
    [ProducesResponseType<ProviderReading>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ProviderReading>> Provider(
        [FromQuery] string match,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(match))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Nothing to read",
                Detail = "Paste a FotMob or Sofascore match link, or a FotMob match id.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        // An unreadable input is the user's to fix and a provider that will not answer is not, so
        // the two are told apart before the request goes out rather than both coming back as "the
        // match could not be read". The panel shows either message beside an otherwise working form.
        if (MatchReference.Parse(match) is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "That is not a match link",
                Detail = "Paste a FotMob or Sofascore match link, or a FotMob match id.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            return Ok(await fotmob.ReadAsync(match, cancellationToken));
        }
        catch (LiveSourceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "The match could not be read",
                Detail = ex.Message,
                Status = StatusCodes.Status502BadGateway,
            });
        }
    }

    /// <summary>
    /// The signed-in user's last reading, plus the token their pushing page needs.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT cross-origin: this is the only place the token exists, and the whole scheme
    /// depends on a page that was not handed it being unable to read it.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<LiveFeedState>(StatusCodes.Status200OK)]
    public ActionResult<LiveFeedState> Get()
    {
        var latest = feed.Latest(user.Id);

        return Ok(new LiveFeedState(
            feed.TokenFor(user.Id),
            latest?.Text,
            latest?.ReceivedUtc,
            latest is null ? null : (DateTime.UtcNow - latest.ReceivedUtc).TotalSeconds,
            latest?.Source));
    }

    /// <summary>
    /// Takes a reading from the page the match is open on.
    /// </summary>
    /// <remarks>
    /// Read as a raw string rather than bound from JSON because the caller sends <c>text/plain</c>:
    /// see <see cref="LiveFeedPush"/> for why. A wrong or missing token is a 401 and nothing is
    /// stored — that is what stops any other page the user happens to visit from pushing numbers
    /// into a tool that sizes stakes.
    ///
    /// The one anonymous endpoint behind the login, and it has to be: it is called from the provider's
    /// page, where the SameSite=Strict login cookie is never sent. The token stands in for the login —
    /// it belongs to one user, and the reading goes to that user and nobody else.
    /// </remarks>
    [HttpPost]
    [AllowAnonymous]
    [EnableCors(LiveFeedCors.PolicyName)]
    [Consumes("text/plain", "application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Push(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        LiveFeedPush? push;
        try
        {
            push = JsonSerializer.Deserialize<LiveFeedPush>(body, Json);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (push is null || string.IsNullOrEmpty(push.Token))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(push.Text))
        {
            return BadRequest();
        }

        return feed.Publish(push.Token, push.Text, push.Source) ? NoContent() : Unauthorized();
    }

    /// <summary>Stops watching. The next reading starts it again.</summary>
    [HttpDelete]
    public IActionResult Clear()
    {
        feed.Clear(user.Id);
        return NoContent();
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>Names the one CORS policy in this app, so the controller and Program cannot drift.</summary>
public static class LiveFeedCors
{
    public const string PolicyName = "live-feed-push";
}
