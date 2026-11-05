using Harbor.Core.Model;

namespace Harbor.Core.Rdp;

/// <summary>Starts a remote desktop session in the system RDP client (not embedded). See ADR-0008.</summary>
public interface IRdpLauncher
{
    /// <param name="overrides">
    /// <c>.rdp</c> lines that win over the entry's settings for this launch only, e.g. the drives a file transfer shares
    /// into the session (SPEC FR-63).
    /// </param>
    Task<IRdpSession> LaunchAsync(ConnectionEntry entry, string? password, IReadOnlyList<string>? overrides = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A launched client process. Completes when the user closes the RDP window.</summary>
public interface IRdpSession
{
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
