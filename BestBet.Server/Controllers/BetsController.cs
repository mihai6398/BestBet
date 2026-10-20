using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

/// <summary>
/// The signed-in user's bets. The owner always comes from the login, never from the request: a bet
/// that belongs to someone else answers 404, exactly like one that does not exist.
/// </summary>
[ApiController]
[Route("api/bets")]
public class BetsController(BetService bets) : ControllerBase
{
    /// <summary>The user's bets, newest first, optionally filtered and paged.</summary>
    /// <remarks>The total number of matching bets is in the <c>X-Total-Count</c> header.</remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BetDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<BetDto>>> List(
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await bets.QueryAsync(new BetListQuery(status, page, pageSize), cancellationToken);
            Response.Headers["X-Total-Count"] = result.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Ok(result.Items);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>Total staked, profit, ROI, yield, open bets, and the CLV scoreboard.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<BetStats>> Stats(CancellationToken cancellationToken)
        => Ok(await bets.StatsAsync(cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<BetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BetDto>> Get(int id, CancellationToken cancellationToken)
        => await bets.GetAsync(id, cancellationToken) is { } bet ? Ok(bet) : NotFound();

    /// <summary>Saves a bet, usually straight from an analysis.</summary>
    [HttpPost]
    [ProducesResponseType<BetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BetDto>> Save(
        [FromBody] SaveBetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await bets.SaveAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Records a bet that was placed at a bookmaker, from what its slip shows, so it can be tracked
    /// with the match as it goes.
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType<BetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BetDto>> Import(
        [FromBody] ImportBetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await bets.ImportAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Corrects what was taken and fills in what was not known at entry: the price actually got, the
    /// stake actually placed, the closing price, the bookmaker, and how the bet settled.
    /// </summary>
    /// <remarks>Every field is optional, so PUT and PATCH do the same thing here.</remarks>
    [HttpPut("{id:int}")]
    [HttpPatch("{id:int}")]
    [ProducesResponseType<BetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BetDto>> Update(
        int id,
        [FromBody] UpdateBetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await bets.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>Sets the final result and works out the payout and the profit or loss.</summary>
    [HttpPost("{id:int}/settle")]
    [ProducesResponseType<BetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BetDto>> Settle(
        int id,
        [FromBody] SettleBetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var settled = await bets.SettleAsync(id, request, cancellationToken);
            return settled is null ? NotFound() : Ok(settled);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Asks what an open bet is worth now, given where the match has got to.
    /// </summary>
    /// <remarks>
    /// A POST that changes nothing, which is unusual enough to say out loud: it takes the current
    /// match state in a body rather than in a query string, and the row it is asked about is left
    /// exactly as it was. Re-run it as often as the match moves.
    /// </remarks>
    [HttpPost("{id:int}/reprice")]
    [ProducesResponseType<RepricedBet>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepricedBet>> Reprice(
        int id,
        [FromBody] RepriceBetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var repriced = await bets.RepriceAsync(id, request, cancellationToken);
            return repriced is null ? NotFound() : Ok(repriced);
        }
        catch (AnalysisInputException ex)
        {
            return Problem(ex);
        }
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        => await bets.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

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
}
