using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Harbor.Windows.Rdp;

/// <summary>
/// What Harbor's Remote Desktop windows share (the transfer window, SPEC FR-65, ADR-0042, and the connection window,
/// SPEC FR-67, ADR-0045): finding the Remote Desktop ActiveX control of Windows (mstscax.dll) and running a window that
/// hosts it on its own UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RdpControl
{
    // Remote Desktop client classes that are not "safe for scripting" (so the password can be set), newest first.
    private static readonly string[] ControlClasses =
    [
        "a0c63c30-f08d-4ab4-907c-34905d770c7d", // MsRdpClient10NotSafeForScripting
        "8b918b82-7985-4c24-89df-c33ad2bbfbcd", // MsRdpClient9NotSafeForScripting
    ];

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The newest registered control class, or <c>null</c> when the control is not installed.</summary>
    public static string? FindClass() => ControlClasses.FirstOrDefault(IsRegistered);

    /// <summary>
    /// Creates a window with <paramref name="create"/> on a new STA thread with its own message loop (the ActiveX control
    /// needs one, and the app's WPF windows stay independent) and returns <paramref name="result"/> of it once it exists.
    /// The task given to <paramref name="create"/> completes when the window has closed.
    /// </summary>
    public static TResult OpenWindow<TForm, TResult>(string threadName, Func<Task, TForm> create, Func<TForm, TResult> result)
        where TForm : Form
    {
        var created = new TaskCompletionSource<TForm>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                using var form = create(closed.Task);
                // The handle may already exist here (the control's EndInit creates it), so its HandleCreated event can have
                // fired before anyone listened, which used to leave the app's UI thread waiting – every other window of the
                // app froze while this one was open. Make sure it exists, then hand the window over at once.
                _ = form.Handle;
                created.TrySetResult(form);
                Application.Run(form);
            }
            catch (Exception ex)
            {
                created.TrySetException(ex);
            }
            finally
            {
                created.TrySetException(new InvalidOperationException("The Remote Desktop window did not open."));
                closed.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = threadName,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Called on the app's UI thread: wait only for the window to be created, never for longer than a few seconds.
        if (!created.Task.Wait(OpenTimeout))
        {
            throw new InvalidOperationException("The Remote Desktop window did not open.");
        }

        return result(created.Task.Result);
    }

    /// <summary>The newest advanced settings the control offers (each version adds settings to the one before).</summary>
    public static dynamic Advanced(dynamic rdp)
    {
        try
        {
            return rdp.AdvancedSettings9;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return rdp.AdvancedSettings2;
        }
    }

    /// <summary>Applies a setting the control may not have; an older control connects without it.</summary>
    public static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException)
        {
            // An older control without this setting: connect without it.
        }
    }

    /// <summary>The control's text for a disconnect reason, or the number when it has none.</summary>
    public static string Describe(RdpControlHost host, int reason)
    {
        try
        {
            var rdp = host.Control;
            string text = rdp.GetErrorDescription((uint)reason, (uint)rdp.ExtendedDisconnectReason);
            return string.IsNullOrWhiteSpace(text) ? $"reason {reason}" : text;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidOperationException)
        {
            return $"reason {reason}";
        }
    }

    private static bool IsRegistered(string clsid)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{{{clsid}}}\InprocServer32");
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary>The control's events a window of the app handles.</summary>
internal interface IRdpControlEvents
{
    void OnControlConnecting();
    void OnControlLoginComplete();
    void OnControlDisconnected(int reason);
}

/// <summary>Events of the Remote Desktop control (dispinterface <c>IMsTscAxEvents</c>), the ones the windows use.</summary>
[ComImport]
[Guid("336d5562-efa8-482e-8cb3-c5c0fc7a7db6")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsTscAxEvents
{
    [DispId(1)] void OnConnecting();
    [DispId(2)] void OnConnected();
    [DispId(3)] void OnLoginComplete();
    [DispId(4)] void OnDisconnected(int discReason);
    [DispId(10)] void OnFatalError(int errorCode);
}

/// <summary>Receives the control's events; members it does not declare are ignored by the control.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class RdpEventSink(IRdpControlEvents target) : IMsTscAxEvents
{
    public void OnConnecting() => target.OnControlConnecting();
    public void OnConnected() { }
    public void OnLoginComplete() => target.OnControlLoginComplete();
    public void OnDisconnected(int discReason) => target.OnControlDisconnected(discReason);
    public void OnFatalError(int errorCode) => target.OnControlDisconnected(-errorCode);
}

/// <summary>The ActiveX control, created from its class id; its events go to <see cref="RdpEventSink"/>.</summary>
internal sealed class RdpControlHost(string clsid, IRdpControlEvents target) : AxHost(clsid)
{
    private ConnectionPointCookie? _events;

    public dynamic Control => GetOcx() ?? throw new InvalidOperationException("The Remote Desktop control did not start.");

    public bool HasEvents => _events is not null;

    protected override void CreateSink()
    {
        try
        {
            _events = new ConnectionPointCookie(GetOcx()!, new RdpEventSink(target), typeof(IMsTscAxEvents));
        }
        catch (Exception ex) when (ex is InvalidCastException or COMException or ArgumentException)
        {
            _events = null; // without events the window still connects; it just cannot follow the sign-in
        }
    }

    protected override void DetachSink()
    {
        _events?.Disconnect();
        _events = null;
    }
}
