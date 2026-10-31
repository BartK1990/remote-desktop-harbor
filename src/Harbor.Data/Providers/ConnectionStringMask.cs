using System.Data.Common;

namespace Harbor.Data.Providers;

/// <summary>
/// Shows a connection string with its secrets replaced, for the read-only preview in Settings: a SQL login password
/// is never displayed (SPEC FR-45, NFR-12).
/// </summary>
public static class ConnectionStringMask
{
    public const string Mask = "********";

    private static readonly string[] SecretKeys = ["password", "pwd"];

    /// <summary>The connection string with every password value replaced by <see cref="Mask"/>; unparsable text gives <c>null</c>.</summary>
    public static string? Apply(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (var key in builder.Keys.Cast<string>().ToList())
            {
                if (SecretKeys.Contains(key, StringComparer.OrdinalIgnoreCase) && builder[key] is string { Length: > 0 }) builder[key] = Mask;
            }

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
