using Harbor.Core.Identity;
using Harbor.Core.Model;
using Harbor.Core.Storage;

namespace Harbor.Core.Services;

public sealed record PresenceOptions(TimeSpan Ttl, TimeSpan HeartbeatInterval)
{
    public static PresenceOptions Default { get; } = new(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(45));

    public static PresenceOptions FromSettings(HarborSettings s)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(30, s.PresenceTtlSeconds));
        // Heartbeat must be comfortably shorter than the TTL, or records flicker to "expired" between beats.
        var beat = TimeSpan.FromSeconds(Math.Clamp(s.PresenceHeartbeatSeconds, 5, (int)(ttl.TotalSeconds / 2)));
        return new PresenceOptions(ttl, beat);
    }
}

/// <summary>
/// Publishes "user X is connected to entry Y" to the shared database while an RDP client runs, refreshing
/// the record's expiry with heartbeats. Readers ignore expired records, so a crashed app stops being
/// listed after one TTL. Failures never block launching. See ADR-0010.
/// The sessions this app started are also kept in memory and always listed — in local mode, for entries kept
/// on this PC only and while the shared database is offline too. SPEC FR-37, ADR-0047.
/// </summary>
public sealed class SessionPresenceService(IPresenceStore? store, PresenceOptions options, string machineName,
    Action<Exception>? onError = null)
{
    private readonly List<PresenceLease> _leases = [];

    /// <summary>True when presence is shared with other users through the shared database.</summary>
    public bool IsShared => store is not null;

    public PresenceOptions Options => options;

    /// <summary>The Remote Desktop sessions started by this app that are still open.</summary>
    public int OpenCount
    {
        get
        {
            lock (_leases) return _leases.Count;
        }
    }

    /// <summary>
    /// The active sessions: the shared records (when <paramref name="includeShared"/> and a shared database is used)
    /// plus this app's own open sessions that are not among them. SPEC FR-32, FR-37
    /// </summary>
    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(bool includeShared = true, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ActiveSession> shared = [];
        if (store is not null && includeShared)
        {
            try
            {
                shared = await store.ListActiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onError?.Invoke(ex);
            }
        }

        var listed = shared.Select(s => s.Id).ToHashSet();
        var own = OwnSessions().Where(s => !listed.Contains(s.Id)).ToList();
        return own.Count == 0 ? shared : [.. shared, .. own];
    }

    /// <summary>This app's open sessions, as presence records that are not shared. SPEC FR-37</summary>
    public IReadOnlyList<ActiveSession> OwnSessions()
    {
        lock (_leases) return _leases.Select(l => l.ToActiveSession()).ToList();
    }

    /// <summary>
    /// Starts tracking <paramref name="user"/> connected to <paramref name="connectionId"/> as the remote account
    /// <paramref name="accountName"/> and, when <paramref name="publish"/> and a shared database is used, publishing it
    /// there. Dispose the lease when the RDP session ends.
    /// </summary>
    public async Task<PresenceLease> BeginAsync(Guid connectionId, string? accountName, UserIdentity user, bool publish = true,
        CancellationToken cancellationToken = default)
    {
        var lease = new PresenceLease(publish ? store : null, options, connectionId, accountName, user, machineName, onError, Forget);
        lock (_leases) _leases.Add(lease);
        try
        {
            await lease.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(lease);
            throw;
        }

        return lease;
    }

    private void Forget(PresenceLease lease)
    {
        lock (_leases) _leases.Remove(lease);
    }

    /// <summary>Best-effort cleanup of long-expired records.</summary>
    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        if (store is null)
        {
            return;
        }

        try
        {
            await store.PurgeExpiredAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onError?.Invoke(ex);
        }
    }
}

public sealed class PresenceLease : IAsyncDisposable
{
    private readonly IPresenceStore? _store;
    private readonly PresenceOptions _options;
    private readonly UserIdentity _user;
    private readonly string _machineName;
    private readonly Action<Exception>? _onError;
    private readonly Action<PresenceLease>? _onEnded;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private volatile bool _registered; // written by the heartbeat loop, read from the UI thread
    private int _disposed;

    internal PresenceLease(IPresenceStore? store, PresenceOptions options, Guid connectionId, string? accountName, UserIdentity user,
        string machineName, Action<Exception>? onError, Action<PresenceLease>? onEnded = null)
    {
        _store = store;
        _options = options;
        ConnectionId = connectionId;
        AccountName = accountName;
        _user = user;
        _machineName = machineName;
        _onError = onError;
        _onEnded = onEnded;
    }

    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid ConnectionId { get; }
    public string? AccountName { get; }
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>True when this lease publishes to the shared database (not in local mode or for entries kept on this PC).</summary>
    public bool IsShared => _store is not null;

    /// <summary>This session as a presence record; it never expires while the lease is open. SPEC FR-37</summary>
    public ActiveSession ToActiveSession() => new(SessionId, ConnectionId, _user.UserName,
        string.IsNullOrWhiteSpace(_user.DisplayName) ? null : _user.DisplayName, _user.Email, _machineName,
        StartedUtc, DateTimeOffset.UtcNow, DateTimeOffset.MaxValue, AccountName);

    /// <summary>True while the shared database holds a live record for this lease.</summary>
    public bool IsPublished => _registered;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_store is null)
        {
            return;
        }

        await TickAsync(cancellationToken).ConfigureAwait(false);
        _loop = Task.Run(() => LoopAsync(_stop.Token), CancellationToken.None);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await TickAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        try
        {
            // Not yet registered (e.g. database was down at launch) or the record vanished: (re)create it.
            if (!_registered || !await _store!.HeartbeatAsync(SessionId, _options.Ttl, token).ConfigureAwait(false))
            {
                await _store!.StartSessionAsync(SessionId, ConnectionId, AccountName, _user, _machineName, _options.Ttl, token).ConfigureAwait(false);
            }

            _registered = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _onError?.Invoke(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _onEnded?.Invoke(this);
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        if (_store is not null && _registered)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _store.EndSessionAsync(SessionId, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The record will expire on its own after one TTL.
                _onError?.Invoke(ex);
            }
        }

        _stop.Dispose();
    }
}
