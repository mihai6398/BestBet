using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

[ApiController]
[Route("api")]
public class AnalysisController(AnalysisService analysis) : ControllerBase
{
    /// <summary>
    /// Prices one outcome: de-vigs the market, estimates our own probability, and returns the edge,
    /// a stake recommendation and a verdict. Recommends only — it never places anything.
    /// </summary>
    [HttpPost("analyze")]
    [ProducesResponseType<AnalyzeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<AnalyzeResponse> Analyze([FromBody] AnalyzeRequest request)
    {
        try
        {
            return Ok(analysis.Analyze(request));
        }
        catch (AnalysisInputException ex)
        {
            // These are all user-input problems ("odds must be > 1.0", "which side is this?"), so the
            // message goes straight back to the form rather than into a log nobody reads.
            return BadRequest(new ProblemDetails
            {
                Title = "The analysis inputs do not make sense",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }
}
