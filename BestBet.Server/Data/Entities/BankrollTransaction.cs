namespace BestBet.Data.Entities;

public enum BankrollTransactionKind
{
    /// <summary>Money put in. The first one is the starting balance.</summary>
    Deposit,
    /// <summary>Money taken out.</summary>
    Withdrawal,
}

/// <summary>
/// Money moved in or out of a user's bankroll by hand.
/// </summary>
/// <remarks>
/// The balance is never stored. It is these rows plus the profit and loss of every settled bet, so it
/// cannot drift away from the bets it is supposed to describe, and a corrected settlement corrects the
/// balance with it.
/// </remarks>
public class BankrollTransaction
{
    public int Id { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public BankrollTransactionKind Kind { get; set; }

    /// <summary>Always positive; <see cref="Kind"/> says which way it went.</summary>
    public decimal Amount { get; set; }

    public string? Note { get; set; }
}
