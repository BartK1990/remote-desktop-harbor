namespace Harbor.Core.Model;

/// <summary>
/// A presence record: someone launched <see cref="ConnectionId"/> and their app keeps the record
/// alive with heartbeats. Records past <see cref="ExpiresUtc"/> are treated as stale. See SPEC FR-30..FR-36.
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
    DateTimeOffset ExpiresUtc)
{
    public string Describe()
    {
        var who = string.IsNullOrWhiteSpace(DisplayName) ? UserName : $"{DisplayName} ({UserName})";
        return string.IsNullOrWhiteSpace(Email) ? who : $"{who} <{Email}>";
    }
}
