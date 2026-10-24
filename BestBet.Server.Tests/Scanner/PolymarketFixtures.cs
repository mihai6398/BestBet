using System.Text.Json;
using BestBet.Services.Scanner;

namespace BestBet.Server.Tests.Scanner;

/// <summary>The captured Polymarket responses in <c>Fixtures/</c>, read the way the clients read them.</summary>
internal static class PolymarketFixtures
{
    public static string Text(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"polymarket-{name}.json"));

    public static T Read<T>(string name) =>
        JsonSerializer.Deserialize<T>(Text(name), PolymarketJson.Options)
        ?? throw new InvalidOperationException($"Fixture {name} is empty.");

    public static List<GammaEvent> Events(string name) => Read<List<GammaEvent>>(name);

    public static GammaEvent Event(string name, string slug) =>
        Events(name).Single(e => e.Slug == slug);

    public static List<ClobBook> Books(string name) => Read<List<ClobBook>>(name);

    /// <summary>The Liverpool–Brighton family, as <c>events?game_id=90114140</c> returned it.</summary>
    public static List<GammaEvent> LiverpoolBrightonGame() => Events("game-liv-bri");

    public static GammaEvent LiverpoolBrighton() => Event("game-liv-bri", "epl-liv-bri-2026-10-25");

    public static GammaEvent LiverpoolBrightonMoreMarkets() =>
        Event("game-liv-bri", "epl-liv-bri-2026-10-25-more-markets");

    public static GammaEvent PaulFritz() => Event("game-paul-fritz", "atp-paul-fritz-2026-10-24");
}
