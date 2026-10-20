using BestBet.Data.Entities;

namespace BestBet.Contracts;

public record BankrollTransactionDto(
    int Id,
    DateTime CreatedUtc,
    BankrollTransactionKind Kind,
    double Amount,
    string? Note);

/// <summary>
/// Where a user's money stands. Worked out on every read from the movements and the settled bets,
/// never stored, so it cannot disagree with them.
/// </summary>
/// <param name="Balance">Deposits, less withdrawals, plus the profit and loss of every settled bet. The
/// bankroll stakes are sized against: money riding on an open bet is still the user's until it loses.</param>
/// <param name="OpenStake">Money riding on open bets right now.</param>
/// <param name="Available">Balance less what is riding on open bets — what could be staked or
/// withdrawn this minute.</param>
/// <param name="HasStartingBalance">False until the first deposit, which is the starting balance. Until
/// then every stake the calculator suggests is zero, and the UI asks for one.</param>
public record BankrollSummary(
    double Balance,
    double Deposits,
    double Withdrawals,
    double SettledProfitLoss,
    double OpenStake,
    double Available,
    bool HasStartingBalance,
    IReadOnlyList<BankrollTransactionDto> Transactions);

/// <summary>Body of <c>POST /api/bankroll/transactions</c>.</summary>
/// <param name="Amount">Positive; the kind says which way it goes.</param>
public record AddBankrollTransactionRequest(BankrollTransactionKind Kind, double Amount, string? Note = null);
