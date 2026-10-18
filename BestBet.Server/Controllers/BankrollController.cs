using BestBet.Contracts;
using BestBet.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Controllers;

/// <summary>The signed-in user's balance and the money they moved in and out by hand.</summary>
[ApiController]
[Route("api/bankroll")]
public class BankrollController(BankrollService bankroll) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BankrollSummary>> Get(CancellationToken cancellationToken)
        => Ok(await bankroll.SummaryAsync(cancellationToken));

    /// <summary>A deposit or a withdrawal. The first deposit is the starting balance.</summary>
    [HttpPost("transactions")]
    [ProducesResponseType<BankrollSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BankrollSummary>> Add(
        [FromBody] AddBankrollTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await bankroll.AddAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, summary);
        }
        catch (AnalysisInputException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The inputs do not make sense",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    [HttpDelete("transactions/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        => await bankroll.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
