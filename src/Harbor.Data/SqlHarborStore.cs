using System.Data.Common;
using Dapper;
using Harbor.Core.Identity;
using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Storage;
using Harbor.Data.Providers;

namespace Harbor.Data;

/// <summary>
/// Provider-neutral store over ADO.NET + Dapper. The same class backs the local SQLite file, the offline
/// cache, and the shared SQL Server database; only <see cref="IDatabaseProvider"/> differs.
/// Timestamps are Unix milliseconds taken from the database clock; ids are stored as text.
/// </summary>
public sealed class SqlHarborStore(IDatabaseProvider provider, string connectionString) : IConnectionStore, IPresenceStore
{
    private const string ConnectionColumns =
        "Id, Name, Folder, Kind, Host, Port, Domain, UserName, RdpSettings, Notes, PasswordCipher, Version, CreatedUtcMs, UpdatedUtcMs, UpdatedBy, IsDeleted";

    private const string SessionColumns =
        "Id, ConnectionId, UserName, DisplayName, Email, MachineName, StartedUtcMs, LastHeartbeatUtcMs, ExpiresUtcMs";

    public IDatabaseProvider Provider => provider;

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = provider.CreateConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static CommandDefinition Cmd(string sql, object? args, CancellationToken ct, DbTransaction? tx = null) =>
        new(sql, args, tx, cancellationToken: ct);

    // ---------------------------------------------------------------- schema

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        if (provider.PrepareSql is { } prepare)
        {
            await db.ExecuteAsync(Cmd(prepare, null, cancellationToken)).ConfigureAwait(false);
        }

        await db.ExecuteAsync(Cmd(provider.EnsureVersionTableSql, null, cancellationToken)).ConfigureAwait(false);
        var current = await db.ExecuteScalarAsync<int?>(Cmd("SELECT MAX(Version) FROM SchemaVersion", null, cancellationToken))
            .ConfigureAwait(false) ?? 0;

        for (var v = current; v < provider.Migrations.Count; v++)
        {
            await using var tx = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await db.ExecuteAsync(Cmd(provider.Migrations[v], null, cancellationToken, tx)).ConfigureAwait(false);
            await db.ExecuteAsync(Cmd("INSERT INTO SchemaVersion (Version) VALUES (@v)", new { v = v + 1 }, cancellationToken, tx))
                .ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- connections

    public async Task<IReadOnlyList<ConnectionEntry>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT {ConnectionColumns} FROM Connections" + (includeDeleted ? "" : " WHERE IsDeleted = 0");
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.QueryAsync<ConnectionRow>(Cmd(sql, null, cancellationToken)).ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).OrderBy(e => e.Folder, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<ConnectionEntry>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.QueryAsync<ConnectionRow>(Cmd(
            $"SELECT {ConnectionColumns} FROM Connections WHERE UpdatedUtcMs > @since",
            new { since = since.ToUnixTimeMilliseconds() }, cancellationToken)).ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<ConnectionEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(db, null, id, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ConnectionEntry?> GetAsync(DbConnection db, DbTransaction? tx, Guid id, CancellationToken ct)
    {
        var row = await db.QuerySingleOrDefaultAsync<ConnectionRow>(Cmd(
            $"SELECT {ConnectionColumns} FROM Connections WHERE Id = @id", new { id = Key(id) }, ct, tx)).ConfigureAwait(false);
        return row?.ToModel();
    }

    public async Task<ConnectionEntry> SaveAsync(ConnectionEntry entry, string modifiedBy, CancellationToken cancellationToken = default)
    {
        var row = ConnectionRow.FromModel(entry);
        row.UpdatedBy = modifiedBy;
        var now = provider.NowUnixMs;

        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (entry.Version == 0)
        {
            try
            {
                await db.ExecuteAsync(Cmd(
                    $"""
                    INSERT INTO Connections ({ConnectionColumns})
                    VALUES (@Id, @Name, @Folder, @Kind, @Host, @Port, @Domain, @UserName, @RdpSettings, @Notes, @PasswordCipher,
                            1, {now}, {now}, @UpdatedBy, 0)
                    """, row, cancellationToken, tx)).ConfigureAwait(false);
            }
            catch (DbException ex) when (provider.IsUniqueViolation(ex))
            {
                throw new ConcurrencyConflictException($"An entry with id {entry.Id} already exists.");
            }
        }
        else
        {
            var affected = await db.ExecuteAsync(Cmd(
                $"""
                UPDATE Connections SET
                    Name = @Name, Folder = @Folder, Kind = @Kind, Host = @Host, Port = @Port, Domain = @Domain,
                    UserName = @UserName, RdpSettings = @RdpSettings, Notes = @Notes, PasswordCipher = @PasswordCipher,
                    Version = Version + 1, UpdatedUtcMs = {now}, UpdatedBy = @UpdatedBy
                WHERE Id = @Id AND Version = @Version AND IsDeleted = 0
                """, row, cancellationToken, tx)).ConfigureAwait(false);
            if (affected == 0)
            {
                throw new ConcurrencyConflictException(
                    $"'{entry.Name}' was changed or deleted by someone else. Reload and try again.");
            }
        }

        var stored = await GetAsync(db, tx, entry.Id, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return stored!;
    }

    public async Task DeleteAsync(Guid id, long expectedVersion, string deletedBy, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var affected = await db.ExecuteAsync(Cmd(
            $"""
            UPDATE Connections SET IsDeleted = 1, PasswordCipher = NULL, Version = Version + 1,
                UpdatedUtcMs = {provider.NowUnixMs}, UpdatedBy = @deletedBy
            WHERE Id = @id AND Version = @expectedVersion AND IsDeleted = 0
            """, new { id = Key(id), expectedVersion, deletedBy }, cancellationToken)).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new ConcurrencyConflictException("The entry was changed or deleted by someone else. Reload and try again.");
        }
    }

    public async Task ApplyReplicaAsync(IEnumerable<ConnectionEntry> entries, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            var row = ConnectionRow.FromModel(entry);
            var affected = await db.ExecuteAsync(Cmd(
                """
                UPDATE Connections SET
                    Name = @Name, Folder = @Folder, Kind = @Kind, Host = @Host, Port = @Port, Domain = @Domain,
                    UserName = @UserName, RdpSettings = @RdpSettings, Notes = @Notes, PasswordCipher = @PasswordCipher,
                    Version = @Version, CreatedUtcMs = @CreatedUtcMs, UpdatedUtcMs = @UpdatedUtcMs, UpdatedBy = @UpdatedBy,
                    IsDeleted = @IsDeleted
                WHERE Id = @Id
                """, row, cancellationToken, tx)).ConfigureAwait(false);
            if (affected == 0)
            {
                await db.ExecuteAsync(Cmd(
                    $"""
                    INSERT INTO Connections ({ConnectionColumns})
                    VALUES (@Id, @Name, @Folder, @Kind, @Host, @Port, @Domain, @UserName, @RdpSettings, @Notes, @PasswordCipher,
                            @Version, @CreatedUtcMs, @UpdatedUtcMs, @UpdatedBy, @IsDeleted)
                    """, row, cancellationToken, tx)).ConfigureAwait(false);
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- vault key + meta

    public async Task<VaultKeyRecord?> GetVaultKeyAsync(string slot, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.QuerySingleOrDefaultAsync<VaultKeyRow>(Cmd(
            "SELECT Slot, KeyId, Scheme, ProtectedKey, CreatedUtcMs FROM VaultKeys WHERE Slot = @slot", new { slot }, cancellationToken))
            .ConfigureAwait(false);
        return row is null
            ? null
            : new VaultKeyRecord(row.Slot, Guid.Parse(row.KeyId), row.Scheme, row.ProtectedKey,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedUtcMs));
    }

    public async Task<bool> TryAddVaultKeyAsync(VaultKeyRecord record, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.ExecuteAsync(Cmd(
                "INSERT INTO VaultKeys (Slot, KeyId, Scheme, ProtectedKey, CreatedUtcMs) VALUES (@Slot, @KeyId, @Scheme, @ProtectedKey, @CreatedUtcMs)",
                VaultKeyRow.FromModel(record), cancellationToken)).ConfigureAwait(false);
            return true;
        }
        catch (DbException ex) when (provider.IsUniqueViolation(ex))
        {
            return false;
        }
    }

    public async Task ReplaceVaultKeyAsync(VaultKeyRecord record, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.ExecuteAsync(Cmd("DELETE FROM VaultKeys WHERE Slot = @Slot", new { record.Slot }, cancellationToken, tx)).ConfigureAwait(false);
        await db.ExecuteAsync(Cmd(
            "INSERT INTO VaultKeys (Slot, KeyId, Scheme, ProtectedKey, CreatedUtcMs) VALUES (@Slot, @KeyId, @Scheme, @ProtectedKey, @CreatedUtcMs)",
            VaultKeyRow.FromModel(record), cancellationToken, tx)).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.ExecuteScalarAsync<string?>(Cmd("SELECT [Value] FROM Meta WHERE [Key] = @key", new { key }, cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var affected = await db.ExecuteAsync(Cmd("UPDATE Meta SET [Value] = @value WHERE [Key] = @key", new { key, value }, cancellationToken, tx))
            .ConfigureAwait(false);
        if (affected == 0)
        {
            await db.ExecuteAsync(Cmd("INSERT INTO Meta ([Key], [Value]) VALUES (@key, @value)", new { key, value }, cancellationToken, tx))
                .ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- presence

    public async Task<ActiveSession> StartSessionAsync(Guid sessionId, Guid connectionId, UserIdentity user, string machineName,
        TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var now = provider.NowUnixMs;
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var args = new
        {
            id = Key(sessionId),
            connectionId = Key(connectionId),
            userName = user.UserName,
            displayName = user.DisplayName,
            email = user.Email,
            machineName,
            ttlMs = (long)ttl.TotalMilliseconds,
        };
        await db.ExecuteAsync(Cmd("DELETE FROM ActiveSessions WHERE Id = @id", args, cancellationToken, tx)).ConfigureAwait(false);
        await db.ExecuteAsync(Cmd(
            $"""
            INSERT INTO ActiveSessions ({SessionColumns})
            VALUES (@id, @connectionId, @userName, @displayName, @email, @machineName, {now}, {now}, {now} + @ttlMs)
            """, args, cancellationToken, tx)).ConfigureAwait(false);
        var row = await db.QuerySingleAsync<SessionRow>(Cmd(
            $"SELECT {SessionColumns} FROM ActiveSessions WHERE Id = @id", args, cancellationToken, tx)).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return row.ToModel();
    }

    public async Task<bool> HeartbeatAsync(Guid sessionId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var now = provider.NowUnixMs;
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var affected = await db.ExecuteAsync(Cmd(
            $"UPDATE ActiveSessions SET LastHeartbeatUtcMs = {now}, ExpiresUtcMs = {now} + @ttlMs WHERE Id = @id",
            new { id = Key(sessionId), ttlMs = (long)ttl.TotalMilliseconds }, cancellationToken)).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.ExecuteAsync(Cmd("DELETE FROM ActiveSessions WHERE Id = @id", new { id = Key(sessionId) }, cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.QueryAsync<SessionRow>(Cmd(
            $"SELECT {SessionColumns} FROM ActiveSessions WHERE ExpiresUtcMs > {provider.NowUnixMs} ORDER BY StartedUtcMs",
            null, cancellationToken)).ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.ExecuteAsync(Cmd(
            $"DELETE FROM ActiveSessions WHERE ExpiresUtcMs < {provider.NowUnixMs} - @olderMs",
            new { olderMs = (long)olderThan.TotalMilliseconds }, cancellationToken)).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- rows

    private static string Key(Guid id) => id.ToString("D");

    private static DateTimeOffset FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    private sealed class ConnectionRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Folder { get; set; }
        public int Kind { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Domain { get; set; }
        public string? UserName { get; set; }
        public string? RdpSettings { get; set; }
        public string? Notes { get; set; }
        public byte[]? PasswordCipher { get; set; }
        public long Version { get; set; }
        public long CreatedUtcMs { get; set; }
        public long UpdatedUtcMs { get; set; }
        public string? UpdatedBy { get; set; }
        public int IsDeleted { get; set; }

        public ConnectionEntry ToModel() => new()
        {
            Id = Guid.Parse(Id),
            Name = Name,
            Folder = Folder,
            Kind = (EntryKind)Kind,
            Host = Host,
            Port = Port,
            Domain = Domain,
            UserName = UserName,
            RdpSettings = RdpSettings,
            Notes = Notes,
            PasswordCipher = PasswordCipher,
            Version = Version,
            CreatedUtc = FromMs(CreatedUtcMs),
            UpdatedUtc = FromMs(UpdatedUtcMs),
            UpdatedBy = UpdatedBy,
            IsDeleted = IsDeleted != 0,
        };

        public static ConnectionRow FromModel(ConnectionEntry e) => new()
        {
            Id = Key(e.Id),
            Name = e.Name,
            Folder = string.IsNullOrWhiteSpace(e.Folder) ? null : e.Folder.Trim(),
            Kind = (int)e.Kind,
            Host = string.IsNullOrWhiteSpace(e.Host) ? null : e.Host.Trim(),
            Port = e.Port,
            Domain = e.Domain,
            UserName = e.UserName,
            RdpSettings = e.RdpSettings,
            Notes = e.Notes,
            PasswordCipher = e.PasswordCipher,
            Version = e.Version,
            CreatedUtcMs = e.CreatedUtc.ToUnixTimeMilliseconds(),
            UpdatedUtcMs = e.UpdatedUtc.ToUnixTimeMilliseconds(),
            UpdatedBy = e.UpdatedBy,
            IsDeleted = e.IsDeleted ? 1 : 0,
        };
    }

    private sealed class SessionRow
    {
        public string Id { get; set; } = "";
        public string ConnectionId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string MachineName { get; set; } = "";
        public long StartedUtcMs { get; set; }
        public long LastHeartbeatUtcMs { get; set; }
        public long ExpiresUtcMs { get; set; }

        public ActiveSession ToModel() => new(Guid.Parse(Id), Guid.Parse(ConnectionId), UserName, DisplayName, Email, MachineName,
            FromMs(StartedUtcMs), FromMs(LastHeartbeatUtcMs), FromMs(ExpiresUtcMs));
    }

    private sealed class VaultKeyRow
    {
        public string Slot { get; set; } = "";
        public string KeyId { get; set; } = "";
        public string Scheme { get; set; } = "";
        public byte[] ProtectedKey { get; set; } = [];
        public long CreatedUtcMs { get; set; }

        public static VaultKeyRow FromModel(VaultKeyRecord r) => new()
        {
            Slot = r.Slot,
            KeyId = Key(r.KeyId),
            Scheme = r.Scheme,
            ProtectedKey = r.ProtectedKey,
            CreatedUtcMs = r.CreatedUtc.ToUnixTimeMilliseconds(),
        };
    }
}
