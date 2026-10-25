using Npgsql;

namespace BestBet.Server;

/// <summary>
/// In development, lets the server run from the IDE read the same <c>.env</c> that docker compose reads.
/// </summary>
/// <remarks>
/// Without this the setup has a trap in it: the guide says to fill in <c>.env</c>, docker compose
/// starts the database from it, and then the server launched with F5 knows nothing about it and stops
/// at "the connection string is not set". Reading the file here makes one file enough in both modes.
///
/// It only ever fills gaps. User-secrets and real environment variables are read first and always
/// win, and outside Development the file is not read at all — in Docker the values arrive as
/// environment variables, as they should.
/// </remarks>
public static class DevelopmentDotEnv
{
    /// <summary>Where docker-compose.yml publishes the database when .env does not say.</summary>
    public const int DefaultPort = 5433;

    /// <summary>Adds what the nearest <c>.env</c> provides and nothing else has set yet. Development only.</summary>
    public static void AddTo(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return;
        }

        var path = FindUpwards(builder.Environment.ContentRootPath, ".env", levels: 3);
        if (path is null)
        {
            return;
        }

        var missing = ToConfiguration(Parse(File.ReadAllText(path)))
            .Where(pair => string.IsNullOrEmpty(builder.Configuration[pair.Key]))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        builder.Configuration.AddInMemoryCollection(missing);
    }

    /// <summary>
    /// Reads <c>KEY=VALUE</c> lines the way docker compose writes them: comments and blank lines are
    /// skipped, and a value wrapped in matching quotes loses them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    /// <summary>
    /// What the server's configuration would call each value. The connection string points at the
    /// database container's port on this machine, the one docker compose publishes.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ToConfiguration(IReadOnlyDictionary<string, string> env)
    {
        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (env.TryGetValue("POSTGRES_USER", out var user) && user.Length > 0
            && env.TryGetValue("POSTGRES_PASSWORD", out var password) && password.Length > 0)
        {
            // The builder escapes the password, so a ';' or a quote in it cannot break the string.
            configuration["ConnectionStrings:BestBet"] = new NpgsqlConnectionStringBuilder
            {
                Host = "localhost",
                // The same default docker-compose.yml publishes the database on.
                Port = int.TryParse(env.GetValueOrDefault("POSTGRES_PORT"), out var port) ? port : DefaultPort,
                Database = NonEmpty(env.GetValueOrDefault("POSTGRES_DB")) ?? "bestbet",
                Username = user,
                Password = password,
            }.ConnectionString;
        }

        Copy(env, "ADMIN_EMAIL", configuration, "ADMIN_EMAIL");
        Copy(env, "ADMIN_PASSWORD", configuration, "ADMIN_PASSWORD");
        Copy(env, "APIFOOTBALL_KEY", configuration, "ApiFootball:Key");
        Copy(env, "AUTH_ALLOW_REGISTRATION", configuration, "Auth:AllowRegistration");

        return configuration;
    }

    private static void Copy(
        IReadOnlyDictionary<string, string> env,
        string from,
        Dictionary<string, string?> configuration,
        string to)
    {
        if (NonEmpty(env.GetValueOrDefault(from)) is { } value)
        {
            configuration[to] = value;
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>The project folder first, then the repository root above it.</summary>
    private static string? FindUpwards(string start, string fileName, int levels)
    {
        var directory = new DirectoryInfo(start);
        for (var level = 0; directory is not null && level <= levels; level++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
