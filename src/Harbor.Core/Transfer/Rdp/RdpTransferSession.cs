using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Core.Transfer.Rdp;

/// <summary>
/// Opens Harbor's own Remote Desktop window for a transfer (SPEC FR-65, ADR-0042): it connects with the PC's drives
/// shared and, once signed in, starts the copy helper in the session itself (Win+R, the start command, Enter), so the
/// user does not have to. Implemented on Windows with the Remote Desktop ActiveX control.
/// </summary>
public interface IRdpTransferSessionHost
{
    /// <summary>False when the Remote Desktop control is not installed; transfers then connect with mstsc (FR-63).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens the window and connects. <paramref name="password"/> is handed to the control in memory only (NFR-12).
    /// Throws when the window cannot be opened.
    /// </summary>
    IRdpTransferSession Open(RdpTransferSessionSettings settings, string? password, RdpTransferSessionRequest request);
}

/// <param name="Title">Window title, e.g. the entry name.</param>
/// <param name="StartCommand">Typed into the session's Run box (<see cref="RdpTransferKit.AutoStartCommand"/>).</param>
/// <param name="ShouldStart">
/// Asked when the session is signed in: false when the helper already runs there (after a reconnect), so it is not
/// started twice. Called on the window's thread.
/// </param>
public sealed record RdpTransferSessionRequest(string Title, string StartCommand, Func<bool> ShouldStart);

/// <summary>What the transfer window is doing. SPEC FR-65</summary>
public enum RdpTransferSessionState
{
    Connecting,

    /// <summary>Signed in; the start command is about to be typed.</summary>
    Starting,

    /// <summary>The start command was typed (or was not needed).</summary>
    Started,

    /// <summary>Signed in, but the window was not in front, so nothing was typed: the user clicks Start copy.</summary>
    NeedsClick,

    Disconnected,
    Closed,
}

/// <summary>Harbor's own transfer window. Completes <see cref="IRdpSession.WaitForExitAsync"/> when it closes.</summary>
public interface IRdpTransferSession : IRdpSession
{
    RdpTransferSessionState State { get; }

    /// <summary>Why the connection ended, when it ended with an error.</summary>
    string? Error { get; }

    /// <summary>Raised on the window's thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>Brings the window to the front.</summary>
    void Show();

    /// <summary>Disconnects (the session and a running copy stay on the server) and closes the window.</summary>
    void Close();
}

/// <summary>
/// Connection settings for the transfer window, taken from an entry (SPEC FR-65, ADR-0042). The window does not use the
/// rest of the entry's <c>.rdp</c> settings: it always shares the drives and the clipboard and uses its own size.
/// </summary>
public sealed record RdpTransferSessionSettings(string Host, int Port, string? UserName, string? Domain, int AuthenticationLevel,
    bool AdministrativeSession)
{
    /// <summary>
    /// The settings for <paramref name="entry"/> (with the account already applied), or <c>null</c> when the window cannot
    /// connect like mstsc would: the entry goes through a Remote Desktop Gateway, or it is not a Remote Desktop entry.
    /// </summary>
    public static RdpTransferSessionSettings? For(ConnectionEntry entry)
    {
        if (!entry.CanLaunch || string.IsNullOrWhiteSpace(entry.Host)) return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entry.Kind == EntryKind.RdpConnection && !string.IsNullOrWhiteSpace(entry.RdpSettings))
        {
            foreach (var (key, _, value, _) in RdpFile.Parse(entry.RdpSettings)) values[key] = value.Trim();
        }

        string? Value(string key) => values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
        int? Number(string key) => int.TryParse(Value(key), out var n) ? n : null;

        // 1 = always, 2 = when the server is not local, 4 = detect: each can route through a gateway the window does not set up.
        if (Value("gatewayhostname") is not null && Number("gatewayusagemethod") is 1 or 2 or 4) return null;

        var user = entry.UserName is { Length: > 0 } u ? u : null;
        var domain = entry.Domain is { Length: > 0 } d ? d : null;
        if (user is not null && domain is null && user.Contains('\\', StringComparison.Ordinal))
        {
            domain = user[..user.IndexOf('\\', StringComparison.Ordinal)];
            user = user[(user.IndexOf('\\', StringComparison.Ordinal) + 1)..];
        }

        return new RdpTransferSessionSettings(entry.Host.Trim(), entry.Port, user, domain,
            Number("authentication level") is { } level and >= 0 and <= 3 ? level : 2,
            Number("administrative session") == 1 || Number("connect to console") == 1);
    }
}
