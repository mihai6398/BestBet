using System.Net;
using System.Text;

namespace BestBet.Server.Tests.Integration;

/// <summary>
/// FotMob, as far as the integration tests are concerned: the captured match responses in
/// <c>Fixtures/</c>, served from memory, so nothing in the suite goes near the network.
/// </summary>
/// <remarks>
/// The match documents are real responses (see the fixture tests). The day list is the one
/// constructed piece — four fixtures in FotMob's shape, standing for whatever day is asked for — and
/// it carries only the three fields the name lookup reads: the id, the two names, and whether the
/// match is on.
/// </remarks>
public sealed class FixtureFotMob : HttpMessageHandler
{
    private static readonly Dictionary<string, string> Matches = new()
    {
        ["6106410"] = "fotmob-first-half-stoppage",
        ["5795478"] = "fotmob-not-started",
        ["6106397"] = "fotmob-finished-red-card",
        ["1000022162"] = "fotmob-live-no-xg",
    };

    private const string Day = """
        {
          "leagues": [
            {
              "name": "Champions League",
              "matches": [
                { "id": 6106410, "home": { "name": "Como" }, "away": { "name": "Manchester United" },
                  "status": { "started": true, "finished": false } },
                { "id": 6106397, "home": { "name": "VfB Stuttgart" }, "away": { "name": "Atlético Madrid" },
                  "status": { "started": true, "finished": true } }
              ]
            },
            {
              "name": "Premier League",
              "matches": [
                { "id": 5795478, "home": { "name": "Everton" }, "away": { "name": "Chelsea" },
                  "status": { "started": false, "finished": false } }
              ]
            },
            {
              "name": "Copa Libertadores",
              "matches": [
                { "id": 1000022162, "home": { "name": "Club Bolivar" }, "away": { "name": "Olimpia" },
                  "status": { "started": true, "finished": false } }
              ]
            }
          ]
        }
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;

        if (path.Contains("/matches?date=", StringComparison.Ordinal))
        {
            return Task.FromResult(Json(Day));
        }

        var id = request.RequestUri.Query.Split("matchId=").ElementAtOrDefault(1);
        if (path.Contains("/matchDetails", StringComparison.Ordinal) && id is not null && Matches.TryGetValue(id, out var fixture))
        {
            return Task.FromResult(Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{fixture}.json"))));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
