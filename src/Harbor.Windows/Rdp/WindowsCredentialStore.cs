using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Harbor.Windows.Interop.NativeMethods;

namespace Harbor.Windows.Rdp;

/// <summary>
/// Writes the <c>TERMSRV/&lt;host&gt;</c> credential that mstsc.exe reads, so the password never appears on a
/// command line or in an .rdp file. Per-user store; no admin rights needed. See ADR-0008.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsCredentialStore
{
    public static unsafe void WriteDomainPassword(string target, string userName, string password, bool persistAcrossLogon)
    {
        fixed (char* pTarget = target)
        fixed (char* pUser = userName)
        fixed (char* pPassword = password)
        {
            var credential = new CREDENTIALW
            {
                Type = CRED_TYPE_DOMAIN_PASSWORD,
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

    public static void Delete(string target)
    {
        if (!CredDelete(target, CRED_TYPE_DOMAIN_PASSWORD, 0) && Marshal.GetLastPInvokeError() != ERROR_NOT_FOUND)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not delete credential {target}.");
        }
    }
}
