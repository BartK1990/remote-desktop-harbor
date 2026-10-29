using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Harbor.Core.Security;
using static Harbor.Windows.Interop.NativeMethods;

namespace Harbor.Windows.Security;

/// <summary>
/// DPAPI-NG ("CNG DPAPI") protection to an Active Directory principal, typically a security group:
/// descriptor <c>SID=S-1-5-21-…</c>. Any member of the group, on any domain-joined machine, can unwrap the
/// shared vault key without a shared secret being distributed. Requires AD with Windows Server 2012+ DCs.
/// See ADR-0006.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiNgKeyProtector(string? descriptor) : IKeyProtector
{
    public const string SchemeName = "dpapi-ng";

    public string Scheme => SchemeName;

    public unsafe byte[] Protect(ReadOnlySpan<byte> key)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            throw new VaultKeyException("A DPAPI-NG protection descriptor (e.g. SID=S-1-5-21-...) must be configured.");
        }

        Check(NCryptCreateProtectionDescriptor(descriptor, 0, out var hDescriptor), "NCryptCreateProtectionDescriptor");
        try
        {
            fixed (byte* p = key)
            {
                Check(NCryptProtectSecret(hDescriptor, NCRYPT_SILENT_FLAG, p, (uint)key.Length, 0, 0, out var blob, out var size),
                    "NCryptProtectSecret");
                return CopyAndFree(blob, size);
            }
        }
        finally
        {
            _ = NCryptCloseProtectionDescriptor(hDescriptor);
        }
    }

    public unsafe byte[] Unprotect(ReadOnlySpan<byte> protectedKey)
    {
        // The descriptor is embedded in the blob, so no configuration is needed to unwrap.
        fixed (byte* p = protectedKey)
        {
            Check(NCryptUnprotectSecret(0, NCRYPT_SILENT_FLAG, p, (uint)protectedKey.Length, 0, 0, out var data, out var size),
                "NCryptUnprotectSecret");
            return CopyAndFree(data, size);
        }
    }

    private static byte[] CopyAndFree(nint ptr, uint size)
    {
        try
        {
            var result = new byte[size];
            Marshal.Copy(ptr, result, 0, (int)size);
            return result;
        }
        finally
        {
            unsafe { new Span<byte>((void*)ptr, (int)size).Clear(); }
            _ = LocalFree(ptr);
        }
    }

    private static void Check(int status, string api)
    {
        if (status != 0)
        {
            throw new VaultKeyException(
                $"{api} failed (0x{status:X8}). The shared vault key is protected to an AD group; make sure this " +
                "account is a member and a domain controller is reachable.");
        }
    }
}
