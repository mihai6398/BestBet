using BestBet.Contracts;
using BestBet.Data;
using BestBet.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Services;

/// <summary>
/// One user's bankroll: the money they moved in and out by hand, and the balance that follows from
/// it and from their settled bets.
/// </summary>
/// <remarks>
/// The sums are done in <see cref="decimal"/> over the stored cents and only turned into JSON numbers
/// at the end, so the balance is exact rather than the sum of a few hundred rounding errors.
/// </remarks>
public class BankrollService(BestBetDbContext db, ICurrentUser user)
{
    /// <summary>A sanity bound, not a limit anyone should meet: past this the input is a typo.</summary>
    public const double MaxAmount = 1_000_000_000.0;

    private IQueryable<BankrollTransaction> Mine => db.BankrollTransactions.Where(t => t.UserId == user.Id);

    public async Task<BankrollSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var transactions = await Mine
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedUtc)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

        var bets = await db.Bets
            .Where(b => b.UserId == user.Id)
            .Select(b => new { b.Status, b.StakeAmount, b.ProfitLoss })
            .ToListAsync(cancellationToken);

        var deposits = transactions.Where(t => t.Kind == BankrollTransactionKind.Deposit).Sum(t => t.Amount);
        var withdrawals = transactions.Where(t => t.Kind == BankrollTransactionKind.Withdrawal).Sum(t => t.Amount);
        var settled = bets.Where(b => b.Status != BetStatus.Open).Sum(b => b.ProfitLoss ?? 0m);
        var open = bets.Where(b => b.Status == BetStatus.Open).Sum(b => b.StakeAmount);
        var balance = deposits - withdrawals + settled;

        return new BankrollSummary(
            (double)balance,
            (double)deposits,
            (double)withdrawals,
            (double)settled,
            (double)open,
            (double)(balance - open),
            transactions.Any(t => t.Kind == BankrollTransactionKind.Deposit),
            transactions.Select(t => new BankrollTransactionDto(t.Id, t.CreatedUtc, t.Kind, (double)t.Amount, t.Note)).ToList());
    }

    public async Task<BankrollSummary> AddAsync(
        AddBankrollTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!(request.Amount > 0.0) || !double.IsFinite(request.Amount) || request.Amount > MaxAmount)
        {
            throw new AnalysisInputException($"The amount must be a positive number, got {request.Amount}.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            throw new AnalysisInputException("A movement is either a deposit or a withdrawal.");
        }

        var note = request.Note?.Trim();
        if (note is { Length: > 200 })
        {
            throw new AnalysisInputException($"The note is limited to 200 characters, got {note.Length}.");
        }

        var amount = BetService.Money(request.Amount);

        // Taking out money that is riding on a bet, or that was never there, would leave a balance
        // that describes nobody's account.
        if (request.Kind == BankrollTransactionKind.Withdrawal)
        {
            var current = await SummaryAsync(cancellationToken);
            if ((double)amount > current.Available + 0.000001)
            {
                throw new AnalysisInputException(
                    $"Only {current.Available:0.00} is available to withdraw; the rest is riding on open bets " +
                    "or was never deposited.");
            }
        }

        db.BankrollTransactions.Add(new BankrollTransaction
        {
            UserId = user.Id,
            CreatedUtc = DateTime.UtcNow,
            Kind = request.Kind,
            Amount = amount,
            Note = string.IsNullOrEmpty(note) ? null : note,
        });

        await db.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Mine.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.BankrollTransactions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
