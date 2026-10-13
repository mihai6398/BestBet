using BestBet.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Data;

/// <summary>
/// Single-user, single-file SQLite store. No auth, no multi-tenancy — v1 runs on one machine
/// for one person, and pretending otherwise would be complexity with no payer.
/// </summary>
public class BestBetDbContext(DbContextOptions<BestBetDbContext> options) : DbContext(options)
{
    public DbSet<PositionLog> Positions => Set<PositionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var position = modelBuilder.Entity<PositionLog>();

        position.HasKey(p => p.Id);

        // The log is read newest-first and filtered on "closing price still missing", which is the
        // query that drives CLV follow-up.
        position.HasIndex(p => p.CreatedUtc);
        position.HasIndex(p => p.Outcome);

        position.Property(p => p.MatchLabel).HasMaxLength(200);
        position.Property(p => p.MarketLabel).HasMaxLength(200);
        position.Property(p => p.Selection).HasMaxLength(64);
        position.Property(p => p.EstimateMethod).HasMaxLength(64);
        position.Property(p => p.EntrySiblingOddsCsv).HasMaxLength(200);
        position.Property(p => p.ClosingSiblingOddsCsv).HasMaxLength(200);
        position.Property(p => p.Notes).HasMaxLength(2000);

        position.Property(p => p.MarketKind).HasConversion<string>();
        position.Property(p => p.Verdict).HasConversion<string>();
        position.Property(p => p.Outcome).HasConversion<string>();
    }
}
