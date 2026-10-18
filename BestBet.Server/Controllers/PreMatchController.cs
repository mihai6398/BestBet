using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

/// <summary>
/// The pre-match model: expected goals for a fixture from team form and the players expected to play.
/// </summary>
[ApiController]
[Route("api/prematch")]
public class PreMatchController(PreMatchService preMatch) : ControllerBase
{
    /// <summary>Whether a key is set, and which competitions can be read.</summary>
    [HttpGet("status")]
    [ProducesResponseType<PreMatchStatus>(StatusCodes.Status200OK)]
    public ActionResult<PreMatchStatus> Status() => Ok(preMatch.Status());

    /// <summary>
    /// Reads the competition's table and both squads and returns each side's expected goals, the
    /// likeliest scorers, and the probabilities those imply.
    /// </summary>
    /// <remarks>
    /// Sends the competition, season and team ids to the data source — nothing about stakes, prices or
    /// the positions log.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<PreMatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PreMatchResponse>> Analyze(
        [FromBody] PreMatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await preMatch.AnalyzeAsync(request, cancellationToken));
        }
        catch (AnalysisInputException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The pre-match analysis could not be run",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }
}
