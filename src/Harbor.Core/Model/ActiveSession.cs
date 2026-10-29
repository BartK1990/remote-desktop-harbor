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
