using System.Diagnostics;
using System.Runtime.Versioning;
using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Windows.Rdp;

/// <summary>
/// Launches the built-in Windows Remote Desktop client (mstsc.exe) as a separate, normal window.
/// Flow: write a temporary .rdp file (no password) → put the password into Credential Manager as
/// <c>TERMSRV/host</c> → start mstsc → clean up the file and (by default) the credential. See ADR-0008.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MstscLauncher(string tempDirectory, bool keepCredential, Action<Exception>? onError = null) : IRdpLauncher
{
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(30);

    public Task<IRdpSession> LaunchAsync(ConnectionEntry entry, string? password, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(tempDirectory);
        var rdpPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.rdp");
        File.WriteAllText(rdpPath, RdpFile.Build(entry));

        var target = RdpFile.CredentialTarget(entry.Host!);
        var wroteCredential = false;
        if (!string.IsNullOrEmpty(password) && entry.QualifiedUserName is { } user)
        {
            WindowsCredentialStore.WriteDomainPassword(target, user, password, keepCredential);
            wroteCredential = true;
        }

        Process process;
        try
        {
            var mstsc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            process = Process.Start(new ProcessStartInfo(mstsc) { ArgumentList = { rdpPath }, UseShellExecute = false })
                ?? throw new InvalidOperationException("mstsc.exe did not start.");
        }
        catch
        {
            TryDelete(rdpPath, wroteCredential && !keepCredential ? target : null);
            throw;
        }

        // mstsc reads the file and credential while connecting; give it time, then remove both.
        _ = Task.Delay(CleanupDelay, CancellationToken.None)
            .ContinueWith(_ => TryDelete(rdpPath, wroteCredential && !keepCredential ? target : null), TaskScheduler.Default);

        return Task.FromResult<IRdpSession>(new MstscSession(process));
    }

    private void TryDelete(string rdpPath, string? credentialTarget)
    {
        try
        {
            File.Delete(rdpPath);
            if (credentialTarget is not null)
            {
                WindowsCredentialStore.Delete(credentialTarget);
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private sealed class MstscSession(Process process) : IRdpSession
    {
        public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            using (process)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
