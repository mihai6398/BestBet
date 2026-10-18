using System.Text.Json.Serialization;
using BestBet.Data;
using BestBet.Server.Controllers;
using BestBet.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums cross the wire as their names ("TotalGoals", "Marginal", "NextGoalAway"), which keeps
        // the API readable by hand and the TypeScript union types honest.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// One SQLite file next to the app. Single user, no auth — see the spec's non-goals.
var connectionString = builder.Configuration.GetConnectionString("BestBet")
                       ?? "Data Source=bestbet.db";
builder.Services.AddDbContext<BestBetDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddScoped<PositionService>();

// Holds the last reading pushed in from a match page the user has open. In memory only: it is a
// snapshot of a match in progress, worthless ten minutes later and not worth a table.
builder.Services.AddSingleton<LiveFeedStore>();

// One CORS policy, for one endpoint: the push from a live-score page. Any origin, because the
// pushing page is whichever provider the user is reading, and the token is what actually guards it
// — a browser sends a simple cross-origin POST regardless of CORS, which only decides who may read
// the reply. POST alone, so nothing else is reachable from a page this app does not control.
builder.Services.AddCors(options => options.AddPolicy(
    LiveFeedCors.PolicyName,
    policy => policy.AllowAnyOrigin().WithMethods("POST").AllowAnyHeader()));

// The outbound dependencies in the app. Short timeouts on purpose: every one of them is a nice to
// have, and a slow third party must never hold up a form the user is filling in while a match runs.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<PolymarketClient>(client =>
{
    client.BaseAddress = new Uri("https://gamma-api.polymarket.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BestBet/1.0 (personal decision-support tool)");
});

// Live match data from a provider that answers a plain request. The base address ends in /api/data/
// because that is where the endpoints are — the shorter /api/matchDetails answers 404.
//
// The user agent is the app's own name, as it is for Polymarket. That is the whole difference
// between this and the route that does not exist for Sofascore: nothing here claims to be a browser
// it is not, so there is nothing to keep working as anyone's bot detection tightens.
builder.Services.AddHttpClient<FotMobClient>(client =>
{
    client.BaseAddress = new Uri("https://www.fotmob.com/api/data/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BestBet/1.0 (personal decision-support tool)");
});

// Player and team statistics for the pre-match model. The key is the user's own and is read from
// configuration (ApiFootball:Key, or the environment variable ApiFootball__Key); it is held by the
// server and never sent to the browser. Without one, the model is simply unavailable and everything
// else works as before.
builder.Services.AddHttpClient<ApiFootballClient>((services, client) =>
    {
        // Overridable so the integration can be exercised against a local stand-in without a key.
        client.BaseAddress = new Uri(
            services.GetRequiredService<IConfiguration>()["ApiFootball:BaseUrl"] ?? "https://v3.football.api-sports.io/");
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddTypedClient((client, services) => new ApiFootballClient(
        client,
        services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
        services.GetRequiredService<IConfiguration>()["ApiFootball:Key"]));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PreMatchService>();

var app = builder.Build();

// No migrations yet: the schema is one table and v1 is a local tool. EnsureCreated is the honest
// choice here; swap to migrations the moment the schema has to survive a change with data in it.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BestBetDbContext>().Database.EnsureCreated();
}

// Chrome asks permission before a public page may reach a private address, and asks it as a
// preflight carrying this header. Answering it is how the push from a provider's page is allowed to
// reach a server on this machine; without it the browser drops the request before it is sent.
//
// Ahead of UseCors, not after it: the CORS middleware answers a preflight and short-circuits the
// pipeline, so anything registered behind it never runs on the one request that needs this header.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }

    await next();
});

app.UseCors();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Redirecting to HTTPS only makes sense when there is an HTTPS endpoint to redirect to. A container
// that publishes plain HTTP has none, and UseHttpsRedirection would then 307 every single request to
// a port nothing is listening on. The URLs Kestrel was actually given are the honest source of truth.
var servesHttps =
    (app.Configuration["ASPNETCORE_URLS"] ?? app.Configuration["urls"] ?? string.Empty)
        .Contains("https://", StringComparison.OrdinalIgnoreCase)
    || !string.IsNullOrWhiteSpace(app.Configuration["ASPNETCORE_HTTPS_PORTS"])
    || !string.IsNullOrWhiteSpace(app.Configuration["HTTPS_PORT"]);

if (servesHttps)
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
