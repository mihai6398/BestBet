using System.Net;
using System.Text;
using System.Text.Json;
using BestBet.Contracts;
using BestBet.Domain;
using BestBet.Services;
using Microsoft.Extensions.Caching.Memory;

namespace BestBet.Server.Tests.Services;

public class PreMatchModelTests
{
    private static readonly LeagueRates League = new(1.5, 1.2);

    private static TeamForm Average(int games = 30) => new(games, games, 1.5, 1.2, 1.2, 1.5);

    [Fact]
    public void Two_average_teams_get_the_league_rates()
    {
        var (home, away) = PreMatchModel.TeamExpectedGoals(Average(), Average(), League);

        Assert.Equal(1.5, home, 2);
        Assert.Equal(1.2, away, 2);
    }

    [Fact]
    public void A_top_attack_against_a_weak_defence_is_expected_to_score_more_than_the_league_average()
    {
        var topHome = new TeamForm(15, 15, 2.8, 0.6, 2.2, 0.9);
        var weakAway = new TeamForm(15, 15, 0.9, 2.0, 0.7, 2.2);

        var (home, away) = PreMatchModel.TeamExpectedGoals(topHome, weakAway, League);

        Assert.True(home > 2.8, $"home was {home}");
        Assert.True(away < 0.7, $"away was {away}");
    }

    [Fact]
    public void A_few_games_move_the_estimate_far_less_than_many_games_of_the_same_form()
    {
        var hot = (int games) => new TeamForm(games, games, 3.0, 0.5, 3.0, 0.5);

        var (few, _) = PreMatchModel.TeamExpectedGoals(hot(2), Average(), League);
        var (many, _) = PreMatchModel.TeamExpectedGoals(hot(30), Average(), League);

        Assert.True(few < many);
        Assert.True(few < 2.0, $"two games should not be trusted that far, got {few}");
    }

    [Fact]
    public void Expected_goals_stay_inside_the_bounds()
    {
        var absurd = new TeamForm(50, 50, 9.0, 0.0, 9.0, 0.0);
        var (home, _) = PreMatchModel.TeamExpectedGoals(absurd, new TeamForm(50, 50, 0.0, 9.0, 0.0, 9.0), League);

        Assert.Equal(PreMatchModel.MaxExpectedGoals, home);
    }

    private static PlayerSeason Striker(int id, int goals, int shots, int minutes = 2700, int apps = 30)
        => new(id, $"Striker {id}", "Attacker", apps, minutes, goals, shots);

    private static PlayerSeason Winger(int id) => new(id, $"Winger {id}", "Midfielder", 30, 2500, 5, 40);

    [Fact]
    public void With_nobody_absent_the_players_share_out_the_team_goals_and_change_nothing()
    {
        var squad = new[] { Striker(1, 20, 90), Striker(2, 8, 50), Winger(3) };

        var adjusted = PreMatchModel.ApplySquad(1.8, 30, squad, new HashSet<int>());

        Assert.Equal(1.8, adjusted.ExpectedGoals, 6);
        Assert.Equal(0.0, adjusted.MissingShare, 6);
        Assert.Equal(1.8, adjusted.Players.Sum(p => -Math.Log(1 - p.ScoringProbability)), 5);
    }

    [Fact]
    public void The_better_scorer_is_the_likelier_scorer()
    {
        var squad = new[] { Striker(1, 22, 100), Striker(2, 6, 40), Winger(3) };

        var players = PreMatchModel.ApplySquad(1.8, 30, squad, new HashSet<int>()).Players;

        Assert.Equal(1, players[0].Id);
        Assert.True(players[0].ScoringProbability > players[1].ScoringProbability);
        Assert.True(players[0].ScoringProbability < 1.0);
        Assert.Equal(1.0 / players[0].ScoringProbability, players[0].FairOdds, 6);
    }

    [Fact]
    public void An_absent_top_scorer_lowers_the_team_but_not_by_his_whole_share()
    {
        var squad = new[] { Striker(1, 22, 100), Striker(2, 6, 40), Winger(3) };

        var without = PreMatchModel.ApplySquad(1.8, 30, squad, new HashSet<int> { 1 });

        Assert.True(without.ExpectedGoals < 1.8);
        Assert.True(without.MissingShare > 0.3);

        // Replacements keep part of it: the team does not lose the entire share.
        Assert.True(without.ExpectedGoals > 1.8 * (1 - without.MissingShare));

        var absentRow = without.Players.Single(p => p.Id == 1);
        Assert.True(absentRow.Absent);
        Assert.Equal(0.0, absentRow.ScoringProbability);
    }

    [Fact]
    public void A_player_who_rarely_plays_is_discounted()
    {
        var regular = Striker(1, 10, 60);
        var rotation = Striker(2, 10, 60, minutes: 500, apps: 12);

        Assert.True(
            PreMatchModel.MatchFraction(rotation, 30) < PreMatchModel.MatchFraction(regular, 30));
    }

    [Fact]
    public void A_goalkeeper_is_never_a_likely_scorer()
    {
        var squad = new[] { Striker(1, 15, 80), new PlayerSeason(9, "Keeper", "Goalkeeper", 30, 2700, 0, 0) };

        var keeper = PreMatchModel.ApplySquad(1.5, 30, squad, new HashSet<int>()).Players.Single(p => p.Id == 9);

        Assert.True(keeper.ScoringProbability < 0.01);
    }

    [Fact]
    public void A_small_sample_of_goals_is_pulled_towards_the_position()
    {
        var lucky = new PlayerSeason(1, "Lucky", "Attacker", 2, 180, 3, 4);   // 1.5 goals per 90 in 2 games

        Assert.True(PreMatchModel.GoalsPer90(lucky) < 0.8);
        Assert.True(PreMatchModel.GoalsPer90(lucky) > PreMatchModel.PositionPriorPer90("Attacker"));
    }

    [Fact]
    public void An_empty_squad_changes_nothing()
    {
        var adjusted = PreMatchModel.ApplySquad(1.4, 10, [], new HashSet<int>());

        Assert.Equal(1.4, adjusted.ExpectedGoals, 6);
        Assert.Empty(adjusted.Players);
    }
}

public class ApiFootballClientTests
{
    /// <summary>Four teams, five home and five away games each: enough for a league average.</summary>
    internal const string StandingsJson = """
        {"errors":[],"response":[{"league":{"id":39,"standings":[[
          {"team":{"id":1,"name":"Manchester City"},"all":{"played":10},
           "home":{"played":5,"goals":{"for":15,"against":3}},"away":{"played":5,"goals":{"for":11,"against":5}}},
          {"team":{"id":2,"name":"Manchester United"},"all":{"played":10},
           "home":{"played":5,"goals":{"for":8,"against":6}},"away":{"played":5,"goals":{"for":5,"against":8}}},
          {"team":{"id":3,"name":"Bayern München"},"all":{"played":10},
           "home":{"played":5,"goals":{"for":10,"against":5}},"away":{"played":5,"goals":{"for":7,"against":6}}},
          {"team":{"id":4,"name":"Sheffield Wednesday"},"all":{"played":10},
           "home":{"played":5,"goals":{"for":4,"against":9}},"away":{"played":5,"goals":{"for":3,"against":12}}}
        ]]}}]}
        """;

    internal const string PlayersJson = """
        {"errors":[],"paging":{"current":1,"total":1},"response":[
          {"player":{"id":100,"name":"E. Haaland"},"statistics":[
            {"games":{"appearences":8,"minutes":700,"position":"Attacker"},"goals":{"total":9},"shots":{"total":30}},
            {"games":{"appearences":2,"minutes":150,"position":"Attacker"},"goals":{"total":null},"shots":{"total":null}}]},
          {"player":{"id":101,"name":"R. Dias"},"statistics":[
            {"games":{"appearences":9,"minutes":810,"position":"Defender"},"goals":{"total":1},"shots":{"total":5}}]}
        ]}
        """;

    [Fact]
    public void Standings_become_per_game_form_and_league_averages()
    {
        var standings = ApiFootballClient.ReadStandings(JsonDocument.Parse(StandingsJson).RootElement);

        Assert.Equal(4, standings.Teams.Count);
        var city = standings.Teams.Single(t => t.Id == 1);
        Assert.Equal(3.0, city.Form.GoalsForHome, 6);
        Assert.Equal(0.6, city.Form.GoalsAgainstHome, 6);
        Assert.Equal(2.2, city.Form.GoalsForAway, 6);

        Assert.NotNull(standings.League);
        Assert.Equal(37.0 / 20.0, standings.League!.HomeGoals, 6);
        Assert.Equal(26.0 / 20.0, standings.League.AwayGoals, 6);
    }

    [Fact]
    public void A_season_barely_started_gives_no_league_average()
    {
        const string json = """
            {"errors":[],"response":[{"league":{"standings":[[
              {"team":{"id":1,"name":"A"},"home":{"played":1,"goals":{"for":3,"against":0}},"away":{"played":0,"goals":{"for":0,"against":0}}}
            ]]}}]}
            """;

        Assert.Null(ApiFootballClient.ReadStandings(JsonDocument.Parse(json).RootElement).League);
    }

    [Fact]
    public void Players_are_summed_over_competitions_and_nulls_count_as_zero()
    {
        var players = ApiFootballClient.ReadPlayers(JsonDocument.Parse(PlayersJson).RootElement);

        var haaland = players.Single(p => p.Id == 100);
        Assert.Equal(10, haaland.Appearances);
        Assert.Equal(850, haaland.Minutes);
        Assert.Equal(9, haaland.Goals);
        Assert.Equal(30, haaland.Shots);
        Assert.Equal("Attacker", haaland.Position);
    }

    [Fact]
    public void An_errors_object_is_read_as_a_message()
    {
        var root = JsonDocument.Parse("""{"errors":{"token":"Error/Missing application key."},"response":[]}""").RootElement;

        Assert.Contains("application key", ApiFootballClient.ReadError(root));
        Assert.Null(ApiFootballClient.ReadError(JsonDocument.Parse("""{"errors":[],"response":[]}""").RootElement));
    }
}

public class PreMatchServiceTests
{
    private sealed class FakeHandler(Func<Uri, string> reply) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string? KeySeen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            KeySeen = request.Headers.TryGetValues(ApiFootballClient.KeyHeader, out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(reply(request.RequestUri!), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (PreMatchService Service, FakeHandler Handler) Build(
        string? key = "test-key",
        Func<Uri, string>? reply = null)
    {
        var handler = new FakeHandler(reply ?? (uri => uri.AbsolutePath.Contains("standings")
            ? ApiFootballClientTests.StandingsJson
            : ApiFootballClientTests.PlayersJson));

        var client = new ApiFootballClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") },
            new MemoryCache(new MemoryCacheOptions()),
            key);

        return (new PreMatchService(client, new FixedClock(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero))), handler);
    }

    [Fact]
    public void The_season_starts_in_july()
    {
        Assert.Equal(2025, PreMatchService.SeasonFor(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(2026, PreMatchService.SeasonFor(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task A_full_run_produces_expected_goals_probabilities_and_scorers()
    {
        var (service, handler) = Build();

        var response = await service.AnalyzeAsync(
            new PreMatchRequest(39, "Man City", "Sheffield Wednesday"),
            CancellationToken.None);

        Assert.Equal(2026, response.Season);
        Assert.Equal("test-key", handler.KeySeen);
        Assert.Equal("Manchester City", response.Home.Name);
        Assert.True(response.Rates.HomeExpectedGoals > response.Rates.AwayExpectedGoals);
        Assert.True(response.Probabilities.HomeWin > 0.7);
        Assert.Equal(1.0, response.Probabilities.HomeWin + response.Probabilities.Draw + response.Probabilities.AwayWin, 3);
        Assert.Equal("E. Haaland", response.Home.Players[0].Name);
        Assert.True(response.Home.Players[0].ScoringProbability > response.Home.Players[1].ScoringProbability);
    }

    [Fact]
    public async Task Marking_the_top_scorer_absent_lowers_the_team_total()
    {
        var (service, _) = Build();

        var present = await service.AnalyzeAsync(new PreMatchRequest(39, "Man City", "Bayern"), CancellationToken.None);
        var absent = await service.AnalyzeAsync(
            new PreMatchRequest(39, "Man City", "Bayern", HomeAbsent: [100]),
            CancellationToken.None);

        Assert.True(absent.Rates.HomeExpectedGoals < present.Rates.HomeExpectedGoals);
        Assert.Equal(present.Rates.AwayExpectedGoals, absent.Rates.AwayExpectedGoals, 9);
    }

    [Fact]
    public async Task Repeating_a_run_does_not_spend_more_of_the_daily_quota()
    {
        var (service, handler) = Build();
        var request = new PreMatchRequest(39, "Man City", "Bayern");

        await service.AnalyzeAsync(request, CancellationToken.None);
        var first = handler.Requests.Count;
        await service.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(first, handler.Requests.Count);
        Assert.Equal(3, first);   // one table, two squads
    }

    [Fact]
    public async Task Without_a_key_the_failure_says_how_to_fix_it()
    {
        var (service, _) = Build(key: null);

        var ex = await Assert.ThrowsAsync<AnalysisInputException>(() =>
            service.AnalyzeAsync(new PreMatchRequest(39, "Man City", "Bayern"), CancellationToken.None));

        Assert.Contains("ApiFootball", ex.Message);
    }

    [Fact]
    public async Task A_squad_that_cannot_be_read_costs_the_players_not_the_answer()
    {
        var (service, _) = Build(reply: uri => uri.AbsolutePath.Contains("standings")
            ? ApiFootballClientTests.StandingsJson
            : """{"errors":{"requests":"You have reached the request limit for the day."},"response":[]}""");

        var response = await service.AnalyzeAsync(new PreMatchRequest(39, "Man City", "Bayern"), CancellationToken.None);

        Assert.Empty(response.Home.Players);
        Assert.Contains(response.Warnings, w => w.Contains("limit"));
        Assert.True(response.Rates.HomeExpectedGoals > 0);
    }

    [Theory]
    [InlineData("Man United", "Manchester United")]
    [InlineData("bayern munchen", "Bayern München")]
    [InlineData("Sheffield Wed", "Sheffield Wednesday")]
    [InlineData("Manchester City FC", "Manchester City")]
    public void Team_names_are_matched_loosely(string typed, string expected)
    {
        var table = ApiFootballClient.ReadStandings(JsonDocument.Parse(ApiFootballClientTests.StandingsJson).RootElement);

        Assert.Equal(expected, PreMatchService.Find(table, typed).Name);
    }

    [Fact]
    public void An_ambiguous_or_unknown_name_lists_the_choices_instead_of_guessing()
    {
        var table = ApiFootballClient.ReadStandings(JsonDocument.Parse(ApiFootballClientTests.StandingsJson).RootElement);

        var ambiguous = Assert.Throws<AnalysisInputException>(() => PreMatchService.Find(table, "Manchester"));
        Assert.Contains("Manchester City", ambiguous.Message);
        Assert.Contains("Manchester United", ambiguous.Message);

        var unknown = Assert.Throws<AnalysisInputException>(() => PreMatchService.Find(table, "Zzz Rovers"));
        Assert.Contains("Bayern", unknown.Message);
    }

    [Fact]
    public void The_analysis_prices_any_market_from_the_pre_match_rates()
    {
        var analysis = new AnalysisService();

        var response = analysis.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Peste 2.5", 1.80, [2.00]),
            null,
            Bankroll: 100,
            Selection: MarketSelection.Over,
            Line: 2.5,
            PreMatch: new PreMatchRates(2.2, 1.1)));

        Assert.Equal(AnalysisService.PreMatchMethod, response.Estimate.Method);
        Assert.InRange(response.Edge.OwnProbability, 0.55, 0.65);

        // And the lower side of the same line is the complement.
        var under = analysis.Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.TotalGoals, "Sub 2.5", 2.00, [1.80]),
            null,
            Bankroll: 100,
            Selection: MarketSelection.Under,
            Line: 2.5,
            PreMatch: new PreMatchRates(2.2, 1.1)));

        Assert.Equal(1.0, response.Edge.OwnProbability + under.Edge.OwnProbability, 6);
    }

    [Fact]
    public void Nonsense_rates_are_refused()
    {
        Assert.Throws<AnalysisInputException>(() => new AnalysisService().Analyze(new AnalyzeRequest(
            new MarketOdds(MarketKind.Moneyline1X2, "1", 2.0, [3.4, 3.8]),
            null,
            Bankroll: 100,
            Selection: MarketSelection.Home,
            PreMatch: new PreMatchRates(0.0, 1.0))));
    }
}
