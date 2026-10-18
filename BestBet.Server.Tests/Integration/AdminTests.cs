using System.Net;
using System.Net.Http.Json;
using BestBet.Contracts;

namespace BestBet.Server.Tests.Integration;

/// <summary>Accounts exist because the admin made them, and a temporary password stays temporary.</summary>
[Collection(ApiCollection.Name)]
public class AdminTests(BestBetFactory app)
{
    [Fact]
    public async Task The_admin_is_created_from_the_environment_and_can_sign_in()
    {
        var admin = await app.AdminAsync();

        var me = (await admin.GetFromJsonAsync<MeResponse>("/api/auth/me", AuthTests.Json))!;

        Assert.Equal(BestBetFactory.AdminEmail, me.Email);
        Assert.True(me.IsAdmin);
        Assert.False(me.MustChangePassword);
    }

    [Fact]
    public async Task A_new_user_has_to_replace_the_temporary_password_before_anything_else_works()
    {
        var admin = await app.AdminAsync();
        var email = $"new-{Guid.NewGuid():N}@bestbet.test";
        const string temporary = "temporary password one";
        const string chosen = "a password only i know";

        var created = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(email, temporary));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var user = app.Browser();
        var login = await user.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, temporary));
        var me = (await login.Content.ReadFromJsonAsync<MeResponse>(AuthTests.Json))!;

        Assert.True(me.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/bets")).StatusCode);

        var changed = await user.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(temporary, chosen));

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<MeResponse>(AuthTests.Json))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/bets")).StatusCode);

        // And the temporary one no longer works.
        var oldPassword = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, temporary));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    [Fact]
    public async Task A_short_password_is_refused()
    {
        var admin = await app.AdminAsync();

        var created = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserRequest($"short-{Guid.NewGuid():N}@bestbet.test", "too short"));

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    [Fact]
    public async Task Only_the_admin_can_manage_accounts()
    {
        var user = await app.SignedInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("x@bestbet.test", BestBetFactory.Password))).StatusCode);
    }

    [Fact]
    public async Task A_disabled_user_cannot_sign_in_until_enabled_again()
    {
        var admin = await app.AdminAsync();
        var email = await app.CreateUserAsync();
        var id = (await admin.GetFromJsonAsync<List<UserSummary>>("/api/admin/users", AuthTests.Json))!
            .Single(u => u.Email == email).Id;

        await admin.PostAsync($"/api/admin/users/{id}/disable", null);
        var whileDisabled = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));

        await admin.PostAsync($"/api/admin/users/{id}/enable", null);
        var afterEnabling = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, BestBetFactory.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, whileDisabled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterEnabling.StatusCode);
    }

    [Fact]
    public async Task A_reset_password_is_temporary_too()
    {
        var admin = await app.AdminAsync();
        var email = await app.CreateUserAsync();
        var id = (await admin.GetFromJsonAsync<List<UserSummary>>("/api/admin/users", AuthTests.Json))!
            .Single(u => u.Email == email).Id;
        const string temporary = "reset by the admin";

        var reset = await admin.PostAsJsonAsync($"/api/admin/users/{id}/reset-password", new ResetPasswordRequest(temporary));
        var login = await app.Browser().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, temporary));

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<MeResponse>(AuthTests.Json))!.MustChangePassword);
    }

    [Fact]
    public async Task The_admin_cannot_disable_themselves()
    {
        var admin = await app.AdminAsync();
        var self = (await admin.GetFromJsonAsync<List<UserSummary>>("/api/admin/users", AuthTests.Json))!
            .Single(u => u.Email == BestBetFactory.AdminEmail).Id;

        var response = await admin.PostAsync($"/api/admin/users/{self}/disable", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
