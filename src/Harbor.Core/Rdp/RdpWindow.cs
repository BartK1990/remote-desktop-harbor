using Harbor.Core.Model;

namespace Harbor.Core.Rdp;

/// <summary>
/// Harbor's own Remote Desktop window for ordinary connections (SPEC FR-67, ADR-0045): a normal, resizable window that
/// can be maximized without going full screen, and whose remote desktop size follows the window. Implemented on Windows
/// with the Remote Desktop ActiveX control, like the transfer window (FR-65).
/// </summary>
public interface IRdpWindowHost
{
    /// <summary>False when the Remote Desktop control is not installed; connections then open in mstsc (FR-10).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens the window and connects. <paramref name="password"/> is handed to the control in memory only (NFR-12).
    /// The session completes when the window closes. Throws when the window cannot be opened.
    /// </summary>
    IRdpSession Open(RdpWindowSettings settings, string? password);
}

/// <summary>
/// What Harbor's Remote Desktop window connects with, taken from an entry and its <c>.rdp</c> settings (SPEC FR-67).
/// Display settings that only make sense for mstsc (full screen, all monitors) are ignored: the window is always a window.
/// </summary>
/// <param name="Title">Window title, e.g. the entry name.</param>
/// <param name="Resolution">A fixed remote desktop size from the entry; <c>null</c> starts at the window's size.</param>
/// <param name="DynamicResolution">The remote desktop is resized to the window whenever the window changes size.</param>
/// <param name="SmartSizing">The picture is scaled to the window when the remote desktop has another size.</param>
/// <param name="KeyboardHook"><c>keyboardhook</c>: 0 this computer, 1 the remote computer, 2 full screen only.</param>
/// <param name="Audio"><c>audiomode</c>: 0 this computer, 1 the remote computer, 2 do not play.</param>
public sealed record RdpWindowSettings(
    string Title,
    string Host,
    int Port,
    string? UserName,
    string? Domain,
    int AuthenticationLevel,
    bool AdministrativeSession,
    RdpResolution? Resolution,
    bool DynamicResolution,
    bool SmartSizing,
    int ColorDepth,
    int Audio,
    bool RecordAudio,
    bool RedirectClipboard,
    bool RedirectPrinters,
    bool RedirectSmartCards,
    bool RedirectDrives,
    int KeyboardHook,
    bool AutoReconnect)
{
    /// <summary>
    /// The settings for <paramref name="entry"/> (with the account already applied), or <c>null</c> when the window cannot
    /// connect like mstsc would: the entry goes through a Remote Desktop Gateway, or it is not a Remote Desktop entry.
    /// </summary>
    public static RdpWindowSettings? For(ConnectionEntry entry)
    {
        if (!entry.CanLaunch || string.IsNullOrWhiteSpace(entry.Host) || UsesGateway(entry)) return null;

        var (options, _) = RdpOptions.Parse(entry.Kind == EntryKind.RdpConnection ? entry.RdpSettings : null);
        var (user, domain) = Account(entry);
        return new RdpWindowSettings(
            Title: string.IsNullOrWhiteSpace(entry.Name) ? entry.Host.Trim() : entry.Name.Trim(),
            Host: entry.Host.Trim(),
            Port: entry.Port,
            UserName: user,
            Domain: domain,
            AuthenticationLevel: (int)options.ServerAuthentication,
            AdministrativeSession: options.AdminSession || Number(entry, "connect to console") == 1,
            Resolution: options.Resolution,
            DynamicResolution: options.DynamicResolution,
            SmartSizing: options.SmartSizing,
            ColorDepth: (int)options.ColorDepth,
            Audio: (int)options.Audio,
            RecordAudio: options.RecordAudio,
            RedirectClipboard: options.RedirectClipboard,
            RedirectPrinters: options.RedirectPrinters,
            RedirectSmartCards: options.RedirectSmartCards,
            RedirectDrives: options.RedirectDrives,
            KeyboardHook: (int)options.Keyboard,
            AutoReconnect: options.AutoReconnect);
    }

    /// <summary>
    /// True when <paramref name="entry"/> connects through a Remote Desktop Gateway, which Harbor's windows do not set up
    /// (SPEC FR-65, FR-67): 1 = always, 2 = when the server is not local, 4 = detect.
    /// </summary>
    public static bool UsesGateway(ConnectionEntry entry) =>
        Value(entry, "gatewayhostname") is not null && Number(entry, "gatewayusagemethod") is 1 or 2 or 4;

    /// <summary>The user name and domain to sign in with; a domain typed as <c>DOMAIN\user</c> is split off.</summary>
    public static (string? UserName, string? Domain) Account(ConnectionEntry entry)
    {
        var user = entry.UserName is { Length: > 0 } u ? u : null;
        var domain = entry.Domain is { Length: > 0 } d ? d : null;
        if (user is not null && domain is null && user.Contains('\\', StringComparison.Ordinal))
        {
            domain = user[..user.IndexOf('\\', StringComparison.Ordinal)];
            user = user[(user.IndexOf('\\', StringComparison.Ordinal) + 1)..];
        }

        return (user, domain);
    }

    private static string? Value(ConnectionEntry entry, string key)
    {
        if (entry.Kind != EntryKind.RdpConnection || string.IsNullOrWhiteSpace(entry.RdpSettings)) return null;
        string? found = null;
        foreach (var (k, _, value, _) in RdpFile.Parse(entry.RdpSettings))
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) found = value.Trim();
        }

        return found is { Length: > 0 } ? found : null;
    }

    private static int? Number(ConnectionEntry entry, string key) => int.TryParse(Value(entry, key), out var n) ? n : null;

    /// <summary>
    /// The remote desktop size for a window area of <paramref name="width"/> × <paramref name="height"/> pixels: within
    /// the limits Remote Desktop accepts, and an even width (required when a session is resized).
    /// </summary>
    public static (int Width, int Height) DesktopSizeFor(int width, int height)
    {
        var w = Math.Clamp(width, RdpOptions.MinDesktopSize, RdpOptions.MaxDesktopSize) & ~1;
        var h = Math.Clamp(height, RdpOptions.MinDesktopSize, RdpOptions.MaxDesktopSize);
        return (w, h);
    }
}
