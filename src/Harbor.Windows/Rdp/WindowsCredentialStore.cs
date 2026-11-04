using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Harbor.Windows.Interop.NativeMethods;

namespace Harbor.Windows.Rdp;

/// <summary>
/// Writes the <c>TERMSRV/&lt;host&gt;</c> credential that mstsc.exe reads, so the password never appears on a
/// command line or in an .rdp file. Per-user store; no admin rights needed. See ADR-0008, ADR-0035.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsCredentialStore
{
    /// <summary>
    /// Stores the password as a <em>generic</em> credential. Windows Defender Credential Guard refuses saved
    /// domain-password credentials for Remote Desktop ("does not allow using saved credentials"), but mstsc
    /// still accepts a generic <c>TERMSRV/&lt;host&gt;</c> one (SPEC FR-11, ADR-0035).
    /// </summary>
    public static unsafe void WritePassword(string target, string userName, string password, bool persistAcrossLogon)
    {
        // A domain-password credential for the same target (written by older versions or saved by mstsc's own
        // "Remember me") would be preferred by mstsc and then rejected under Credential Guard.
        DeleteType(target, CRED_TYPE_DOMAIN_PASSWORD);

        fixed (char* pTarget = target)
        fixed (char* pUser = userName)
        fixed (char* pPassword = password)
        {
            var credential = new CREDENTIALW
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = pTarget,
                UserName = pUser,
                CredentialBlob = (byte*)pPassword,
                CredentialBlobSize = (uint)(password.Length * sizeof(char)),
                // Session persistence: removed automatically at logoff even if the app crashes before cleanup.
                Persist = persistAcrossLogon ? CRED_PERSIST_LOCAL_MACHINE : CRED_PERSIST_SESSION,
            };

            if (!CredWrite(&credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not store credential for {target}.");
            }
        }
    }

    /// <summary>Removes the app's credential for <paramref name="target"/> (both types; missing is fine).</summary>
    public static void Delete(string target)
    {
        DeleteType(target, CRED_TYPE_GENERIC);
        DeleteType(target, CRED_TYPE_DOMAIN_PASSWORD);
    }

    private static void DeleteType(string target, uint type)
    {
        if (!CredDelete(target, type, 0) && Marshal.GetLastPInvokeError() != ERROR_NOT_FOUND)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not delete credential {target}.");
        }
    }
}
