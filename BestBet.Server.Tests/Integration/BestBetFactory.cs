using System.Net.Http.Json;
using BestBet.Contracts;
using BestBet.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using BestBet.Services;
using Testcontainers.PostgreSql;

namespace BestBet.Server.Tests.Integration;

/// <summary>
/// The whole app, hosted in memory, against a real PostgreSQL in a throwaway container — the same
/// database engine, migrations and Identity stores it runs on for real.
/// </summary>
/// <remarks>
/// One container for every integration test: starting PostgreSQL is the slow part. Tests stay
/// independent by making their own users with unique emails rather than by resetting the database.
/// </remarks>
public sealed class BestBetFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@bestbet.test";
    public const string AdminPassword = "correct horse battery staple";

    /// <summary>Long enough for the policy, and shared by every user the tests create.</summary>
    public const string Password = "a long enough test password";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("bestbet_tests")
        .WithUsername("bestbet")
        .WithPassword("bestbet-tests")
        .Build();

    private readonly string _keys = Path.Combine(Path.GetTempPath(), "bestbet-test-keys-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync() => await _database.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        try
        {
            Directory.Delete(_keys, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:BestBet", _database.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("ADMIN_EMAIL", AdminEmail);
        builder.UseSetting("ADMIN_PASSWORD", AdminPassword);
        builder.UseSetting("DataProtection:KeysPath", _keys);
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Warning");

        // Every test client comes from the same address, so the per-address limits would otherwise
        // be shared by the whole suite. The limiter has a test of its own with a low limit.
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");

        // FotMob is served from the captured fixtures: no test may depend on a match being on.
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<FotMobClient>().ConfigurePrimaryHttpMessageHandler(() => new FixtureFotMob()));
    }

    /// <summary>
    /// A browser-like client: keeps cookies, talks HTTPS (the session cookie is Secure and would not
    /// be sent otherwise), and sends the app's anti-forgery header unless told not to.
    /// </summary>
    public HttpClient Browser(bool withAppHeader = true)
    {
        var handlers = new List<DelegatingHandler>();
        if (withAppHeader)
        {
            handlers.Add(new AppHeaderHandler());
        }

        handlers.Add(new CookieContainerHandler());
        return CreateDefaultClient(new Uri("https://localhost"), handlers.ToArray());
    }

    /// <summary>A user with a password of their own, made directly through Identity.</summary>
    public async Task<string> CreateUserAsync(string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@bestbet.test";

        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var result = await users.CreateAsync(new AppUser { UserName = email, Email = email }, Password);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
        return email;
    }

    /// <summary>A fresh user, signed in, with a client that carries their session.</summary>
    public async Task<HttpClient> SignedInAsync(string? email = null)
    {
        email = await CreateUserAsync(email);
        var client = Browser();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    public async Task<HttpClient> AdminAsync()
    {
        var client = Browser();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(AdminEmail, AdminPassword));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private sealed class AppHeaderHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("X-BestBet-Csrf", "1");
            return base.SendAsync(request, cancellationToken);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BestBetFactory>
{
    public const string Name = "api";
}
