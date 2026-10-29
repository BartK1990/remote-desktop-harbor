using Harbor.Core.Services;

namespace Harbor.Data.Tests;

// SPEC FR-30..FR-36
public class PresenceTests
{
    [Fact]
    public async Task Active_session_is_listed_with_user_details_until_ended()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var connectionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await db.Store.StartSessionAsync(sessionId, connectionId, Users.Alice, "PC-01", TimeSpan.FromMinutes(2));
        var s = Assert.Single(await db.Store.ListActiveAsync());
        Assert.Equal(connectionId, s.ConnectionId);
        Assert.Equal("CONTOSO\\alice", s.UserName);
        Assert.Equal("alice@contoso.com", s.Email);
        Assert.Equal("Alice Smith", s.DisplayName);
        Assert.Equal("PC-01", s.MachineName);
        Assert.InRange((s.ExpiresUtc - s.StartedUtc).TotalSeconds, 119, 121);

        await db.Store.EndSessionAsync(sessionId);
        Assert.Empty(await db.Store.ListActiveAsync());
    }

    [Fact]
    public async Task Expired_session_is_hidden_and_heartbeat_revives_it()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var sessionId = Guid.NewGuid();

        await db.Store.StartSessionAsync(sessionId, Guid.NewGuid(), Users.Bob, "PC-02", TimeSpan.FromSeconds(-1));
        Assert.Empty(await db.Store.ListActiveAsync());

        Assert.True(await db.Store.HeartbeatAsync(sessionId, TimeSpan.FromMinutes(1)));
        Assert.Single(await db.Store.ListActiveAsync());

        Assert.False(await db.Store.HeartbeatAsync(Guid.NewGuid(), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Purge_removes_only_long_expired_records()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        await db.Store.StartSessionAsync(Guid.NewGuid(), Guid.NewGuid(), Users.Bob, "PC", TimeSpan.FromDays(-2));
        await db.Store.StartSessionAsync(Guid.NewGuid(), Guid.NewGuid(), Users.Bob, "PC", TimeSpan.FromSeconds(-5));
        await db.Store.StartSessionAsync(Guid.NewGuid(), Guid.NewGuid(), Users.Bob, "PC", TimeSpan.FromMinutes(5));

        Assert.Equal(1, await db.Store.PurgeExpiredAsync(TimeSpan.FromDays(1)));
        Assert.Single(await db.Store.ListActiveAsync());
    }

    [Fact]
    public async Task Lease_publishes_heartbeats_and_ends_on_dispose()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var service = new SessionPresenceService(db.Store, new PresenceOptions(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200)), "PC-03");
        var connectionId = Guid.NewGuid();

        var lease = await service.BeginAsync(connectionId, Users.Alice);
        Assert.True(lease.IsPublished);
        await Task.Delay(1500); // longer than the TTL: only heartbeats keep it alive
        var s = Assert.Single(await service.ListActiveAsync());
        Assert.Equal(connectionId, s.ConnectionId);

        await lease.DisposeAsync();
        Assert.Empty(await service.ListActiveAsync());
    }

    [Fact]
    public async Task Lease_registers_once_database_comes_back()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var remote = new SwitchableStore(db.Store) { Offline = true };
        var errors = 0;
        var service = new SessionPresenceService(remote, new PresenceOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100)), "PC",
            _ => Interlocked.Increment(ref errors));

        await using var lease = await service.BeginAsync(Guid.NewGuid(), Users.Alice);
        Assert.False(lease.IsPublished);
        Assert.True(errors > 0);

        remote.Offline = false;
        await Task.Delay(500);
        Assert.True(lease.IsPublished);
        Assert.Single(await service.ListActiveAsync());
    }
}
