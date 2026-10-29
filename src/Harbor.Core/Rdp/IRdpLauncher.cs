using Harbor.Core.Model;

namespace Harbor.Core.Rdp;

/// <summary>Starts a remote desktop session in the system RDP client (not embedded). See ADR-0008.</summary>
public interface IRdpLauncher
{
    Task<IRdpSession> LaunchAsync(ConnectionEntry entry, string? password, CancellationToken cancellationToken = default);
}

/// <summary>A launched client process. Completes when the user closes the RDP window.</summary>
public interface IRdpSession
{
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
