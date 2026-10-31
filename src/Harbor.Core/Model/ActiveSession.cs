namespace Harbor.Core.Model;

/// <summary>
/// A presence record: someone launched <see cref="ConnectionId"/> and their app keeps the record
/// alive with heartbeats. Records past <see cref="ExpiresUtc"/> are treated as stale. See SPEC FR-30..FR-36.
/// <see cref="AccountName"/> is the remote account used (SPEC FR-09); <c>null</c> in records written by older clients.
/// </summary>
public sealed record ActiveSession(
    Guid Id,
    Guid ConnectionId,
    string UserName,
    string? DisplayName,
    string? Email,
    string MachineName,
    DateTimeOffset StartedUtc,
    DateTimeOffset LastHeartbeatUtc,
    DateTimeOffset ExpiresUtc,
    string? AccountName = null)
{
    /// <summary>
    /// Whether this session uses the remote account <paramref name="qualifiedUserName"/>, i.e. connecting with it would
    /// take this session over. A record without an account (older client) counts as a match. SPEC FR-33
    /// </summary>
    public bool UsesAccount(string? qualifiedUserName) =>
        AccountName is null || string.Equals(AccountName, qualifiedUserName, StringComparison.OrdinalIgnoreCase);

    public string Describe()
    {
        var who = string.IsNullOrWhiteSpace(DisplayName) ? UserName : $"{DisplayName} ({UserName})";
        return string.IsNullOrWhiteSpace(Email) ? who : $"{who} <{Email}>";
    }
}

/// <summary>Helpers for lists of <see cref="ActiveSession"/>.</summary>
public static class ActiveSessionExtensions
{
    /// <summary>
    /// The sessions that connecting as <paramref name="qualifiedUserName"/> would take over: those using the same
    /// account, those that name no account (older clients) and, without a user name, all of them. SPEC FR-33
    /// </summary>
    public static IReadOnlyList<ActiveSession> TakenOverBy(this IEnumerable<ActiveSession> sessions, string? qualifiedUserName) =>
        sessions.Where(s => qualifiedUserName is null || s.UsesAccount(qualifiedUserName)).ToList();
}
