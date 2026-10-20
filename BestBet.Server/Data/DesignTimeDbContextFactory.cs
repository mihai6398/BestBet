using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BestBet.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the app.
/// </summary>
/// <remarks>
/// Adding a migration only needs the provider, not a database, so this needs no secrets and connects
/// to nothing. Applying migrations is done by the app itself on start, with the real connection string.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BestBetDbContext>
{
    public BestBetDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<BestBetDbContext>()
            .UseNpgsql("Host=localhost;Database=bestbet_design_time")
            .Options);
}
