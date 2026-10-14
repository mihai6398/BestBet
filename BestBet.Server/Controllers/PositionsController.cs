using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

[ApiController]
[Route("api/positions")]
public class PositionsController(PositionService positions) : ControllerBase
{
    /// <summary>Every saved analysis, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> List(CancellationToken cancellationToken)
        => Ok(await positions.ListAsync(cancellationToken));

    /// <summary>The CLV scoreboard across the whole log.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<PositionsSummary>> Summary(CancellationToken cancellationToken)
        => Ok(await positions.SummariseAsync(cancellationToken));

    /// <summary>Saves an analysis the user chose to keep.</summary>
    [HttpPost]
    public async Task<ActionResult<PositionDto>> Save(
        [FromBody] SavePositionRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await positions.SaveAsync(request, cancellationToken);
        return CreatedAtAction(nameof(List), new { id = saved.Id }, saved);
    }

    /// <summary>
    /// Records a bet that was placed at a bookmaker, from what its slip shows, so it can be tracked
    /// with the match as it goes.
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType<PositionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PositionDto>> Import(
        [FromBody] ImportPositionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await positions.ImportAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { id = saved.Id }, saved);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Corrects what was taken and fills in what was not known at entry: the price actually got, the
    /// stake actually placed, the closing price, and how the bet settled.
    /// </summary>
    [HttpPatch("{id:int}")]
    [ProducesResponseType<PositionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PositionDto>> Update(
        int id,
        [FromBody] UpdatePositionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await positions.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Asks what an open position is worth now, given where the match has got to.
    /// </summary>
    /// <remarks>
    /// A POST that changes nothing, which is unusual enough to say out loud: it takes the current
    /// match state in a body rather than in a query string, and the row it is asked about is left
    /// exactly as it was. Re-run it as often as the match moves.
    /// </remarks>
    [HttpPost("{id:int}/reprice")]
    [ProducesResponseType<RepricedPosition>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RepricedPosition>> Reprice(
        int id,
        [FromBody] RepricePositionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var repriced = await positions.RepriceAsync(id, request, cancellationToken);
            return repriced is null ? NotFound() : Ok(repriced);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Input problems go back as the message that explains them: every one of these is something a
    /// person typed, and a log entry nobody reads is no help to them.
    /// </summary>
    private ObjectResult Problem(AnalysisInputException ex) => BadRequest(new ProblemDetails
    {
        Title = "The inputs do not make sense",
        Detail = ex.Message,
        Status = StatusCodes.Status400BadRequest,
    });

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        => await positions.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
