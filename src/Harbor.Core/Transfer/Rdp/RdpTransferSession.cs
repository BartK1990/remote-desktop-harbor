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
/// <param name="ResetStarted">
/// Called on the window's thread right before the start command is typed: forgets an earlier start (SPEC FR-66).
/// </param>
/// <param name="HasStarted">
/// Polled on the window's thread after the start command was typed: true once it ran in this session (SPEC FR-66). When it
/// stays false, the keys went somewhere else — for example a full-screen Remote Desktop window inside the session.
/// </param>
public sealed record RdpTransferSessionRequest(string Title, string StartCommand, Func<bool> ShouldStart, Action? ResetStarted = null,
    Func<bool>? HasStarted = null);

/// <summary>What the transfer window is doing. SPEC FR-65</summary>
public enum RdpTransferSessionState
{
    Connecting,

    /// <summary>Signed in; the start command is about to be typed.</summary>
    Starting,

    /// <summary>The start command was typed and is waiting to be confirmed by the session (SPEC FR-66).</summary>
    Checking,

    /// <summary>The start command ran in the session (or was not needed).</summary>
    Started,

    /// <summary>
    /// Signed in, but nothing was typed (the window was not in front), or what was typed did not start the copy in this
    /// session (SPEC FR-66): the user clicks Start copy.
    /// </summary>
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

        // Each gateway usage can route through a gateway the window does not set up.
        if (RdpWindowSettings.UsesGateway(entry)) return null;

        var (user, domain) = RdpWindowSettings.Account(entry);
        return new RdpTransferSessionSettings(entry.Host.Trim(), entry.Port, user, domain,
            Number("authentication level") is { } level and >= 0 and <= 3 ? level : 2,
            Number("administrative session") == 1 || Number("connect to console") == 1);
    }
}
