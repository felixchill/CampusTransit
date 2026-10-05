namespace CampusTransit.Data;

/// <summary>
/// Chooses the database provider at startup. Locally the app uses SQLite; when a hosting
/// platform injects a PostgreSQL URL (Neon, Supabase, Render Postgres) the app switches to
/// PostgreSQL, so data survives restarts and redeploys on hosts with no persistent disk.
/// </summary>
public static class DatabaseSetup
{
    public const string SqliteDefault = "Data Source=campustransit.db";

    /// <summary>Platform-injected URL first, then local configuration, then SQLite.</summary>
    public static string Resolve(IConfiguration configuration)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var configured = configuration.GetConnectionString("Default");
        return string.IsNullOrWhiteSpace(configured) ? SqliteDefault : configured.Trim();
    }

    /// <summary>True for a PostgreSQL URI or an Npgsql keyword/value string.</summary>
    public static bool IsPostgres(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Npgsql only understands keyword/value strings while every hosted provider hands out a
    /// postgres:// URI, so translate it. URI parameters other than sslmode are libpq-specific
    /// and would be rejected as unknown keywords.
    /// </summary>
    public static string NormalizePostgres(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var credentials = uri.UserInfo.Split(':', 2);

        var parts = new List<string>
        {
            $"Host={uri.Host}",
            // Uri reports -1 for schemes it does not know, such as postgres.
            $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
            $"Database={Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))}",
            $"SSL Mode={SslMode(ReadParameter(uri, "sslmode"))}"
        };

        if (credentials.Length > 0 && credentials[0].Length > 0)
        {
            parts.Add($"Username={Uri.UnescapeDataString(credentials[0])}");
        }

        if (credentials.Length > 1)
        {
            parts.Add($"Password={Uri.UnescapeDataString(credentials[1])}");
        }

        return string.Join(';', parts);
    }

    /// <summary>A summary safe to write to logs: the password is never included.</summary>
    public static string Describe(string connectionString) =>
        IsPostgres(connectionString)
            ? string.Join(';', NormalizePostgres(connectionString)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("Password=", StringComparison.OrdinalIgnoreCase)))
            : connectionString;

    private static string? ReadParameter(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1])
            .FirstOrDefault();

    private static string SslMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "disable" => "Disable",
        "allow" => "Allow",
        "prefer" => "Prefer",
        "verify-ca" => "VerifyCA",
        "verify-full" => "VerifyFull",
        _ => "Require"
    };
}
