using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

/// <summary>
/// Reference lines: the same outcome priced somewhere sharper than the book being bet into.
/// </summary>
[ApiController]
[Route("api/reference")]
public class ReferenceController(PolymarketClient polymarket) : ControllerBase
{
    /// <summary>
    /// Searches Polymarket for markets matching a match or team name.
    /// </summary>
    /// <remarks>
    /// The query leaves this machine — it is the only request this app makes to anyone. Nothing else
    /// goes with it: no bankroll, no stake, no saved position, no identifier. What comes back is a
    /// list to choose from, and choosing is the user's.
    /// </remarks>
    [HttpGet("polymarket")]
    [ProducesResponseType<IReadOnlyList<PredictionMarketDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<PredictionMarketDto>>> Polymarket(
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await polymarket.SearchAsync(q, cancellationToken));
        }
        catch (ReferenceSourceException ex)
        {
            // A reference that cannot be fetched is a missing opinion, not a broken analysis, so it
            // comes back as a plain explanation the panel can show beside an otherwise working form.
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "The reference source is not available",
                Detail = ex.Message,
                Status = StatusCodes.Status502BadGateway,
            });
        }
    }
}
