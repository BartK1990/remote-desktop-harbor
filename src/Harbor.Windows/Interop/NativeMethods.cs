using System.Runtime.InteropServices;

namespace Harbor.Windows.Interop;

internal static unsafe partial class NativeMethods
{
    // ---------------------------------------------------------------- DPAPI-NG (ncrypt.dll)

    internal const uint NCRYPT_SILENT_FLAG = 0x00000040;

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int NCryptCreateProtectionDescriptor(string pwszDescriptorString, uint dwFlags, out nint phDescriptor);

    [LibraryImport("ncrypt.dll")]
    internal static partial int NCryptCloseProtectionDescriptor(nint hDescriptor);

    [LibraryImport("ncrypt.dll")]
    internal static partial int NCryptProtectSecret(nint hDescriptor, uint dwFlags, byte* pbData, uint cbData,
        nint pMemPara, nint hWnd, out nint ppbProtectedBlob, out uint pcbProtectedBlob);

    [LibraryImport("ncrypt.dll")]
    internal static partial int NCryptUnprotectSecret(nint phDescriptor, uint dwFlags, byte* pbProtectedBlob, uint cbProtectedBlob,
        nint pMemPara, nint hWnd, out nint ppbData, out uint pcbData);

    [LibraryImport("kernel32.dll")]
    internal static partial nint LocalFree(nint hMem);

    // ---------------------------------------------------------------- Credential Manager (advapi32.dll)

    internal const uint CRED_TYPE_DOMAIN_PASSWORD = 2;
    internal const uint CRED_PERSIST_SESSION = 1;
    internal const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    internal const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CREDENTIALW
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CredWrite(CREDENTIALW* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CredDelete(string target, uint type, uint flags);

    // ---------------------------------------------------------------- secur32.dll

    internal const int NameDisplay = 3;
    internal const int NameUserPrincipal = 8;

    [LibraryImport("secur32.dll", EntryPoint = "GetUserNameExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetUserNameEx(int nameFormat, char* lpNameBuffer, ref uint nSize);

    internal static string? GetUserNameEx(int format)
    {
        uint size = 512;
        var buffer = stackalloc char[(int)size];
        return GetUserNameEx(format, buffer, ref size) && size > 0 ? new string(buffer, 0, (int)size) : null;
    }
}
