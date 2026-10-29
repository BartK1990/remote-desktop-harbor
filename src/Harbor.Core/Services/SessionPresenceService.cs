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
/// </summary>
public sealed class SessionPresenceService(IPresenceStore? store, PresenceOptions options, string machineName,
    Action<Exception>? onError = null)
{
    public bool IsEnabled => store is not null;
    public PresenceOptions Options => options;

    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        if (store is null)
        {
            return [];
        }

        try
        {
            return await store.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onError?.Invoke(ex);
            return [];
        }
    }

    /// <summary>Starts publishing presence. Dispose the lease when the RDP session ends.</summary>
    public async Task<PresenceLease> BeginAsync(Guid connectionId, UserIdentity user, CancellationToken cancellationToken = default)
    {
        var lease = new PresenceLease(store, options, connectionId, user, machineName, onError);
        await lease.StartAsync(cancellationToken).ConfigureAwait(false);
        return lease;
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
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private bool _registered;
    private int _disposed;

    internal PresenceLease(IPresenceStore? store, PresenceOptions options, Guid connectionId, UserIdentity user,
        string machineName, Action<Exception>? onError)
    {
        _store = store;
        _options = options;
        ConnectionId = connectionId;
        _user = user;
        _machineName = machineName;
        _onError = onError;
    }

    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid ConnectionId { get; }

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
                await _store!.StartSessionAsync(SessionId, ConnectionId, _user, _machineName, _options.Ttl, token).ConfigureAwait(false);
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
