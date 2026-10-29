using System.Collections.Concurrent;

namespace Harbor.Data.Providers;

/// <summary>Registry of SQL engines the app can use. See ADR-0004.</summary>
public static class DatabaseProviders
{
    private static readonly ConcurrentDictionary<string, IDatabaseProvider> Registered = new(StringComparer.OrdinalIgnoreCase)
    {
        [SqliteProvider.ProviderName] = new SqliteProvider(),
        [SqlServerProvider.ProviderName] = new SqlServerProvider(),
    };

    public static IDatabaseProvider Sqlite => Registered[SqliteProvider.ProviderName];

    public static IEnumerable<string> Names => Registered.Keys.Order(StringComparer.OrdinalIgnoreCase);

    public static void Register(IDatabaseProvider provider) => Registered[provider.Name] = provider;

    public static IDatabaseProvider Get(string name) =>
        Registered.TryGetValue(name, out var p) ? p : throw new NotSupportedException($"Database provider '{name}' is not registered.");
}
