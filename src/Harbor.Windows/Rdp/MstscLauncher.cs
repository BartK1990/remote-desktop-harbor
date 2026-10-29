using System.Collections.Concurrent;
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

    // Accounts of one host share the TERMSRV/<host> credential (SPEC FR-09, ADR-0019). Each target maps to the launch
    // whose credential is waiting for cleanup, so an older launch's cleanup leaves a newer launch's credential alone.
    private static readonly ConcurrentDictionary<string, long> PendingCleanup = new(StringComparer.OrdinalIgnoreCase);
    private static long _launchCount;

    public Task<IRdpSession> LaunchAsync(ConnectionEntry entry, string? password, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(tempDirectory);
        var rdpPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.rdp");
        File.WriteAllText(rdpPath, RdpFile.Build(entry));

        var target = RdpFile.CredentialTarget(entry.Host!);
        var launch = Interlocked.Increment(ref _launchCount);
        var wroteCredential = false;
        if (!string.IsNullOrEmpty(password) && entry.QualifiedUserName is { } user)
        {
            WindowsCredentialStore.WriteDomainPassword(target, user, password, keepCredential);
            wroteCredential = true;
            if (!keepCredential) PendingCleanup[target] = launch;
        }
        else if (PendingCleanup.TryRemove(target, out _))
        {
            // Another account's credential from a launch moments ago would sign this one in as that account.
            WindowsCredentialStore.Delete(target);
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
            TryDelete(rdpPath, OwnsCredential() ? target : null);
            throw;
        }

        // mstsc reads the file and credential while connecting; give it time, then remove both.
        _ = Task.Delay(CleanupDelay, CancellationToken.None)
            .ContinueWith(_ => TryDelete(rdpPath, OwnsCredential() ? target : null), TaskScheduler.Default);

        return Task.FromResult<IRdpSession>(new MstscSession(process));

        // True only while no later launch has replaced this launch's credential.
        bool OwnsCredential() => wroteCredential && !keepCredential && PendingCleanup.TryRemove(new KeyValuePair<string, long>(target, launch));
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
