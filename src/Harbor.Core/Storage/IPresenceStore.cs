using Harbor.Core.Identity;
using Harbor.Core.Model;

namespace Harbor.Core.Storage;

/// <summary>
/// Shared "who is connected where" records with a server-clock expiry. See ADR-0010.
/// All times are computed from the database clock so clients with skewed clocks agree.
/// </summary>
public interface IPresenceStore
{
    Task<ActiveSession> StartSessionAsync(Guid sessionId, Guid connectionId, UserIdentity user, string machineName,
        TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Extends the expiry. Returns false if the record no longer exists (e.g. purged).</summary>
    Task<bool> HeartbeatAsync(Guid sessionId, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Only records that have not expired.</summary>
    Task<IReadOnlyList<ActiveSession>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes records that expired more than <paramref name="olderThan"/> ago.</summary>
    Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}
