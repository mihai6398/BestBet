using System.Text.Json;
using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

/// <summary>
/// The live feed: readings pushed in from a match page the user has open in their own browser.
/// </summary>
[ApiController]
[Route("api/live-feed")]
public class LiveFeedController(LiveFeedStore feed) : ControllerBase
{
    /// <summary>
    /// The last reading, plus the token the pushing page needs.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT cross-origin: this is the only place the token exists, and the whole scheme
    /// depends on a page that was not handed it being unable to read it.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<LiveFeedState>(StatusCodes.Status200OK)]
    public ActionResult<LiveFeedState> Get()
    {
        var latest = feed.Latest;

        return Ok(new LiveFeedState(
            feed.Token,
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
    /// </remarks>
    [HttpPost]
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

        if (push is null || !string.Equals(push.Token, feed.Token, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(push.Text))
        {
            return BadRequest();
        }

        feed.Publish(push.Text, push.Source);
        return NoContent();
    }

    /// <summary>Stops watching. The next reading starts it again.</summary>
    [HttpDelete]
    public IActionResult Clear()
    {
        feed.Clear();
        return NoContent();
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>Names the one CORS policy in this app, so the controller and Program cannot drift.</summary>
public static class LiveFeedCors
{
    public const string PolicyName = "live-feed-push";
}
