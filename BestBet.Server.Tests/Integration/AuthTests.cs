using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BestBet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Server.Tests.Integration;

/// <summary>Nobody gets in without signing in, and signing in cannot be guessed at.</summary>
[Collection(ApiCollection.Name)]
public class AuthTests(BestBetFactory app)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // --- Closed by default --------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/bets")]
    [InlineData("GET", "/api/bets/1")]
    [InlineData("GET", "/api/bets/stats")]
    [InlineData("GET", "/api/bankroll")]
    [InlineData("GET", "/api/live-feed")]
    [InlineData("GET", "/api/prematch/status")]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/analyze")]
    [InlineData("POST", "/api/bets")]
    [InlineData("DELETE", "/api/bets/1")]
    [InlineData("GET", "/api/an-endpoint-that-does-not-exist")]
    public async Task Without_signing_in_the_api_answers_401(string method, string path)
    {
        var response = await app.Browser().SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_api_answers_401_rather_than_redirecting_to_a_login_page()
    {
        var response = await app.Browser().GetAsync("/api/bets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task The_login_page_itself_is_reachable_without_signing_in()
    {
        // The SPA routes /login on the client, so the server has to hand out its shell to anyone. In
        // the test host there is no built client, so the shell is a 404 — anything but a 401.
        var response = await app.Browser().GetAsync("/login");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Signing in ---------------------------------------------------------

    [Fact]
    public async Task The_right_password_signs_in_and_the_session_answers_who_is_signed_in()
    {
        var email = await app.CreateUserAsync();
        var client = app.Browser();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", Json);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(email, me!.Email);
        Assert.False(me.IsAdmin);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bets")).StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_cannot_be_read_by_script_or_sent_cross_site()
    {
        var email = await app.CreateUserAsync();

        var login = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bestbet.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_exactly_the_same_answer()
    {
        // Anything that told the two apart would let the endpoint be used to find out who has an account.
        var email = await app.CreateUserAsync();

        var wrongPassword = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "not the password at all"));
        var unknownEmail = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@bestbet.test", "not the password at all"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            (await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail,
            (await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_one()
    {
        var email = await app.CreateUserAsync();
        var client = app.Browser();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, $"wrong guess number {attempt}"));
        }

        var rightPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, rightPassword.StatusCode);
    }

    [Fact]
    public async Task Four_wrong_passwords_do_not_lock_it()
    {
        var email = await app.CreateUserAsync();
        var client = app.Browser();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, $"wrong guess number {attempt}"));
        }

        var rightPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));

        Assert.Equal(HttpStatusCode.OK, rightPassword.StatusCode);
    }

    [Fact]
    public async Task Sign_in_is_rate_limited_per_address()
    {
        using var strict = app.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "3"));
        var client = strict.CreateDefaultClient(new Uri("https://localhost"), new HeaderHandler());

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@bestbet.test", "whatever it is"));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var client = await app.SignedInAsync();

        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bets")).StatusCode);
    }

    [Fact]
    public async Task Public_sign_up_is_off_unless_configured()
    {
        var response = await app.Browser().PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("someone@bestbet.test", BestBetFactory.Password));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- The request has to come from the app -------------------------------

    [Fact]
    public async Task A_state_changing_request_without_the_app_header_is_refused()
    {
        var email = await app.CreateUserAsync();

        var response = await app.Browser(withAppHeader: false)
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_live_feed_push_is_the_one_exception_and_needs_its_token_instead()
    {
        // It comes from a provider's page, which can neither send the cookie nor add the header.
        var response = await app.Browser(withAppHeader: false).PostAsync(
            "/api/live-feed",
            new StringContent("""{"token":"not-a-real-token","text":"minute 55"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var response = await app.Browser().GetAsync("/api/auth/me");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    private sealed class HeaderHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("X-BestBet-Csrf", "1");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
