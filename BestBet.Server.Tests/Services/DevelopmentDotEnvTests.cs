using BestBet.Server;
using Npgsql;

namespace BestBet.Server.Tests.Services;

/// <summary>The .env that docker compose reads, read again by the server run from the IDE.</summary>
public class DevelopmentDotEnvTests
{
    [Fact]
    public void Reads_keys_and_values_and_skips_comments_and_blank_lines()
    {
        var env = DevelopmentDotEnv.Parse("""
            # PostgreSQL
            POSTGRES_USER=bestbet

            POSTGRES_PASSWORD = spaced out
            export ADMIN_EMAIL=you@example.com
            not a setting
            """);

        Assert.Equal("bestbet", env["POSTGRES_USER"]);
        Assert.Equal("spaced out", env["POSTGRES_PASSWORD"]);
        Assert.Equal("you@example.com", env["ADMIN_EMAIL"]);
        Assert.Equal(3, env.Count);
    }

    [Fact]
    public void Matching_quotes_around_a_value_are_dropped_and_an_equals_sign_inside_it_is_kept()
    {
        var env = DevelopmentDotEnv.Parse("A=\"quoted value\"\nB='single'\nC=abc==\nD=\"unbalanced'");

        Assert.Equal("quoted value", env["A"]);
        Assert.Equal("single", env["B"]);
        Assert.Equal("abc==", env["C"]);
        Assert.Equal("\"unbalanced'", env["D"]);
    }

    [Fact]
    public void Windows_line_endings_do_not_end_up_in_the_values()
    {
        var env = DevelopmentDotEnv.Parse("POSTGRES_USER=bestbet\r\nPOSTGRES_PORT=5433\r\n");

        Assert.Equal("bestbet", env["POSTGRES_USER"]);
        Assert.Equal("5433", env["POSTGRES_PORT"]);
    }

    [Fact]
    public void The_connection_string_points_at_the_database_port_compose_publishes()
    {
        var configuration = DevelopmentDotEnv.ToConfiguration(new Dictionary<string, string>
        {
            ["POSTGRES_USER"] = "bestbet",
            ["POSTGRES_PASSWORD"] = "secret",
            ["POSTGRES_PORT"] = "5433",
            ["POSTGRES_DB"] = "bets",
        });

        var connection = new NpgsqlConnectionStringBuilder(configuration["ConnectionStrings:BestBet"]);

        Assert.Equal("localhost", connection.Host);
        Assert.Equal(5433, connection.Port);
        Assert.Equal("bets", connection.Database);
        Assert.Equal("bestbet", connection.Username);
        Assert.Equal("secret", connection.Password);
    }

    [Fact]
    public void A_password_with_separators_in_it_cannot_break_the_connection_string()
    {
        var configuration = DevelopmentDotEnv.ToConfiguration(new Dictionary<string, string>
        {
            ["POSTGRES_USER"] = "bestbet",
            ["POSTGRES_PASSWORD"] = "a;b=c'd\"e",
        });

        var connection = new NpgsqlConnectionStringBuilder(configuration["ConnectionStrings:BestBet"]);

        Assert.Equal("a;b=c'd\"e", connection.Password);
        Assert.Equal(5432, connection.Port);
        Assert.Equal("bestbet", connection.Database);
    }

    [Fact]
    public void Without_a_user_and_a_password_there_is_no_connection_string_to_make()
    {
        var configuration = DevelopmentDotEnv.ToConfiguration(new Dictionary<string, string>
        {
            ["POSTGRES_USER"] = "bestbet",
            ["POSTGRES_PASSWORD"] = "",
        });

        Assert.False(configuration.ContainsKey("ConnectionStrings:BestBet"));
    }

    [Fact]
    public void The_admin_the_api_key_and_the_sign_up_switch_carry_over_and_empty_ones_do_not()
    {
        var configuration = DevelopmentDotEnv.ToConfiguration(new Dictionary<string, string>
        {
            ["ADMIN_EMAIL"] = "you@example.com",
            ["ADMIN_PASSWORD"] = "a long enough password",
            ["APIFOOTBALL_KEY"] = "",
            ["AUTH_ALLOW_REGISTRATION"] = "false",
        });

        Assert.Equal("you@example.com", configuration["ADMIN_EMAIL"]);
        Assert.Equal("a long enough password", configuration["ADMIN_PASSWORD"]);
        Assert.Equal("false", configuration["Auth:AllowRegistration"]);
        Assert.False(configuration.ContainsKey("ApiFootball:Key"));
    }
}
