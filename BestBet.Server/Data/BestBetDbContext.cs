using BestBet.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Data;

/// <summary>
/// PostgreSQL store: Identity's tables, and each user's bets and bankroll movements.
/// </summary>
/// <remarks>
/// Every row of user data carries its owner's id with a foreign key to the user, so deleting a user
/// takes their data with them and nothing can point at an account that does not exist.
/// </remarks>
public class BestBetDbContext(DbContextOptions<BestBetDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>Money: two decimals is a cent, and 16 digits before it is more than any bankroll.</summary>
    private const int MoneyPrecision = 18;
    private const int MoneyScale = 2;

    /// <summary>Prices: bookmakers quote to three decimals at most; four leaves room for a de-vig.</summary>
    private const int OddsPrecision = 10;
    private const int OddsScale = 4;

    public DbSet<Bet> Bets => Set<Bet>();

    public DbSet<BankrollTransaction> BankrollTransactions => Set<BankrollTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var bet = modelBuilder.Entity<Bet>();

        bet.HasKey(b => b.Id);
        bet.HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);

        // Every read is one user's bets, newest first or filtered by status, so the owner leads both.
        bet.HasIndex(b => new { b.UserId, b.CreatedUtc });
        bet.HasIndex(b => new { b.UserId, b.Status });

        bet.Property(b => b.Sport).HasMaxLength(64);
        bet.Property(b => b.Competition).HasMaxLength(128);
        bet.Property(b => b.MatchLabel).HasMaxLength(200);
        bet.Property(b => b.MarketLabel).HasMaxLength(200);
        bet.Property(b => b.Selection).HasMaxLength(64);
        bet.Property(b => b.Bookmaker).HasMaxLength(64);
        bet.Property(b => b.EstimateMethod).HasMaxLength(64);
        bet.Property(b => b.EntrySiblingOddsCsv).HasMaxLength(200);
        bet.Property(b => b.ClosingSiblingOddsCsv).HasMaxLength(200);
        bet.Property(b => b.Notes).HasMaxLength(2000);

        bet.Property(b => b.EntryDecimalOdds).HasPrecision(OddsPrecision, OddsScale);
        bet.Property(b => b.ClosingDecimalOdds).HasPrecision(OddsPrecision, OddsScale);
        bet.Property(b => b.BankrollAtEntry).HasPrecision(MoneyPrecision, MoneyScale);
        bet.Property(b => b.StakeAmount).HasPrecision(MoneyPrecision, MoneyScale);
        bet.Property(b => b.Payout).HasPrecision(MoneyPrecision, MoneyScale);
        bet.Property(b => b.ProfitLoss).HasPrecision(MoneyPrecision, MoneyScale);

        bet.Property(b => b.MarketKind).HasConversion<string>().HasMaxLength(32);
        bet.Property(b => b.Verdict).HasConversion<string>().HasMaxLength(16);
        bet.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);

        var transaction = modelBuilder.Entity<BankrollTransaction>();

        transaction.HasKey(t => t.Id);
        transaction.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasIndex(t => new { t.UserId, t.CreatedUtc });
        transaction.Property(t => t.Amount).HasPrecision(MoneyPrecision, MoneyScale);
        transaction.Property(t => t.Kind).HasConversion<string>().HasMaxLength(16);
        transaction.Property(t => t.Note).HasMaxLength(200);
    }
}
