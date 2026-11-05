using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Harbor.Core.Transfer;

namespace Harbor.Windows.Transfer;

/// <summary>
/// Connects <c>\\host\share</c> with an entry's account before a file transfer (and again before every retry), using
/// <c>WNetAddConnection2</c>: the password goes straight to Windows, never on a command line or to disk (NFR-12). Connections
/// it made are closed on <see cref="Dispose"/>. SPEC FR-60, ADR-0038
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class NetworkShareConnector : IDisposable
{
    private const int RESOURCETYPE_DISK = 1;
    private const int CONNECT_TEMPORARY = 4;
    private const int ERROR_ALREADY_ASSIGNED = 85;
    private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_INVALID_PASSWORD = 86;
    private const int ERROR_LOGON_FAILURE = 1326;
    private const int ERROR_ACCOUNT_RESTRICTION = 1327;
    private const int ERROR_BAD_NET_NAME = 67;

    private readonly Lock _gate = new();
    private readonly HashSet<string> _connected = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCEW
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public nint lpLocalName;
        public nint lpRemoteName;
        public nint lpComment;
        public nint lpProvider;
    }

    [LibraryImport("mpr.dll", EntryPoint = "WNetAddConnection2W", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetAddConnection2(ref NETRESOURCEW netResource, string? password, string? userName, int flags);

    [LibraryImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetCancelConnection2(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    /// <summary>
    /// Makes sure the share of every UNC path in <paramref name="paths"/> is connected, as <paramref name="userName"/> when
    /// given (otherwise as the signed-in Windows user). An existing connection with other credentials is used as it is.
    /// </summary>
    /// <exception cref="IOException">The share cannot be reached (retried by the transfer queue).</exception>
    /// <exception cref="UnauthorizedAccessException">The account was refused.</exception>
    public void Connect(IEnumerable<string> paths, string? userName, string? password)
    {
        foreach (var root in paths.Select(UncPath.ShareRoot).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ConnectShare(root, string.IsNullOrWhiteSpace(userName) ? null : userName, string.IsNullOrEmpty(userName) ? null : password);
        }
    }

    private void ConnectShare(string root, string? userName, string? password)
    {
        var remote = Marshal.StringToHGlobalUni(root);
        try
        {
            var resource = new NETRESOURCEW { dwType = RESOURCETYPE_DISK, lpRemoteName = remote };
            var result = WNetAddConnection2(ref resource, password, userName, CONNECT_TEMPORARY);
            switch (result)
            {
                case 0:
                    lock (_gate) _connected.Add(root);
                    return;
                case ERROR_ALREADY_ASSIGNED or ERROR_SESSION_CREDENTIAL_CONFLICT:
                    return; // already connected (perhaps as another user): use that connection
                case ERROR_ACCESS_DENIED or ERROR_INVALID_PASSWORD or ERROR_LOGON_FAILURE or ERROR_ACCOUNT_RESTRICTION:
                    throw new UnauthorizedAccessException($"{root}: {new Win32Exception(result).Message}");
                case ERROR_BAD_NET_NAME:
                    throw new TransferFailedException($"{root}: {new Win32Exception(result).Message}");
                default:
                    throw new IOException($"{root}: {new Win32Exception(result).Message}", new Win32Exception(result));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(remote);
        }
    }

    public void Dispose()
    {
        string[] roots;
        lock (_gate)
        {
            roots = [.. _connected];
            _connected.Clear();
        }

        foreach (var root in roots)
        {
            _ = WNetCancelConnection2(root, 0, force: false);
        }
    }
}
