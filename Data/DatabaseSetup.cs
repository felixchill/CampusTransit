namespace CampusTransit.Data;

/// <summary>
/// Chooses the database provider at startup. Locally the app uses SQLite; when a hosting
/// platform injects a PostgreSQL URL, the app switches to PostgreSQL.
/// </summary>
public static class DatabaseSetup
{
    public const string SqliteDefault = "Data Source=campustransit.db";

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

    public static bool IsPostgres(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

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

    public static string Describe(string connectionString) =>
        IsPostgres(connectionString)
            ? string.Join(';', NormalizePostgres(connectionString)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("Password=", StringComparison.OrdinalIgnoreCase)))
            : connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
                ? connectionString
                : "configured";

    private static string? ReadParameter(Uri uri, string name)
    {
        foreach (var parameter in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = parameter.Split('=', 2);
            if (parts.Length == 2
                && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string SslMode(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "disable" => "Disable",
            "allow" => "Allow",
            "prefer" => "Prefer",
            "verify-ca" => "VerifyCA",
            "verify-full" => "VerifyFull",
            _ => "Require"
        };
}
