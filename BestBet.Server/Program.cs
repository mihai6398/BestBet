using System.Text.Json.Serialization;
using BestBet.Data;
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

// The one outbound dependency in the app. A short timeout on purpose: a reference line is a nice to
// have, and a slow exchange must never hold up a form the user is filling in while a match runs.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<PolymarketClient>(client =>
{
    client.BaseAddress = new Uri("https://gamma-api.polymarket.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BestBet/1.0 (personal decision-support tool)");
});

var app = builder.Build();

// No migrations yet: the schema is one table and v1 is a local tool. EnsureCreated is the honest
// choice here; swap to migrations the moment the schema has to survive a change with data in it.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BestBetDbContext>().Database.EnsureCreated();
}

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
