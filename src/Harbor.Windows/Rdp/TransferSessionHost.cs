using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Harbor.Core.Transfer.Rdp;
using Harbor.Windows.Interop;
using Microsoft.Win32;

namespace Harbor.Windows.Rdp;

/// <summary>
/// Harbor's own Remote Desktop window for file transfers (SPEC FR-65, ADR-0042). It hosts the Remote Desktop ActiveX
/// control that ships with Windows (mstscax.dll) on its own UI thread, connects with every drive of this PC and the
/// clipboard shared, and once the user is signed in types Win+R, Ctrl+V (the start command) and Enter into the session,
/// so the copy helper starts by itself. Everything else about copying stays as in FR-63.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RdpTransferSessionHost : IRdpTransferSessionHost
{
    // Remote Desktop client classes that are not "safe for scripting" (so the password can be set), newest first.
    private static readonly string[] ControlClasses =
    [
        "a0c63c30-f08d-4ab4-907c-34905d770c7d", // MsRdpClient10NotSafeForScripting
        "8b918b82-7985-4c24-89df-c33ad2bbfbcd", // MsRdpClient9NotSafeForScripting
    ];

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);

    private readonly Lazy<string?> _clsid = new(() => ControlClasses.FirstOrDefault(IsRegistered));

    public bool IsAvailable => _clsid.Value is not null;

    public IRdpTransferSession Open(RdpTransferSessionSettings settings, string? password, RdpTransferSessionRequest request)
    {
        var clsid = _clsid.Value ?? throw new InvalidOperationException("The Remote Desktop control is not installed on this PC.");
        var created = new TaskCompletionSource<TransferSessionForm>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Own STA thread with its own message loop: the ActiveX control needs one, and the WPF window stays independent.
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new TransferSessionForm(clsid, settings, password, request, closed.Task);
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
                created.TrySetException(new InvalidOperationException("The Remote Desktop transfer window did not open."));
                closed.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "Remote Desktop transfer window",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Called on the app's UI thread: wait only for the window to be created, never for longer than a few seconds.
        if (!created.Task.Wait(OpenTimeout))
        {
            throw new InvalidOperationException("The Remote Desktop transfer window did not open.");
        }

        return created.Task.Result.Session;
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

/// <summary>Events of the Remote Desktop control (dispinterface <c>IMsTscAxEvents</c>), the ones the window uses.</summary>
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
internal sealed class RdpEventSink(TransferSessionForm form) : IMsTscAxEvents
{
    public void OnConnecting() => form.OnControlConnecting();
    public void OnConnected() { }
    public void OnLoginComplete() => form.OnControlLoginComplete();
    public void OnDisconnected(int discReason) => form.OnControlDisconnected(discReason);
    public void OnFatalError(int errorCode) => form.OnControlDisconnected(-errorCode);
}

/// <summary>The ActiveX control, created from its class id; its events go to <see cref="RdpEventSink"/>.</summary>
internal sealed class RdpControlHost(string clsid, TransferSessionForm form) : AxHost(clsid)
{
    private ConnectionPointCookie? _events;

    public dynamic Control => GetOcx() ?? throw new InvalidOperationException("The Remote Desktop control did not start.");

    public bool HasEvents => _events is not null;

    protected override void CreateSink()
    {
        try
        {
            _events = new ConnectionPointCookie(GetOcx()!, new RdpEventSink(form), typeof(IMsTscAxEvents));
        }
        catch (Exception ex) when (ex is InvalidCastException or COMException or ArgumentException)
        {
            _events = null; // without events the window still connects; the user then starts the copy with the button
        }
    }

    protected override void DetachSink()
    {
        _events?.Disconnect();
        _events = null;
    }
}

/// <summary>The transfer window: a status bar with Start copy / Reconnect, and the remote desktop below it.</summary>
[SupportedOSPlatform("windows")]
internal sealed class TransferSessionForm : Form
{
    private static readonly TimeSpan SettleAfterLogin = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RunBoxDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    private readonly RdpTransferSessionSettings _settings;
    private readonly RdpTransferSessionRequest _request;
    private readonly string? _password;
    private readonly RdpControlHost _rdp;
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _start = new() { Text = "Start copy", AutoSize = true, Dock = DockStyle.Right };
    private readonly Button _reconnect = new() { Text = "Reconnect", AutoSize = true, Dock = DockStyle.Right, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new();
    private Action? _next;
    private bool _closing;

    public TransferSessionForm(string clsid, RdpTransferSessionSettings settings, string? password, RdpTransferSessionRequest request,
        Task closed)
    {
        ClosedTask = closed;
        _settings = settings;
        _password = password;
        _request = request;
        Session = new TransferSession(this);

        Text = $"{request.Title} - file transfer - Remote Desktop Harbor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 840);
        MinimumSize = new Size(640, 480);
        ShowIcon = false;

        var bar = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 6, 10, 6) };
        bar.Controls.Add(_status);
        bar.Controls.Add(_reconnect);
        bar.Controls.Add(_start);
        _start.Click += (_, _) => StartCopy(clicked: true);
        _reconnect.Click += (_, _) => Connect();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var next = _next;
            _next = null;
            next?.Invoke();
        };

        _rdp = new RdpControlHost(clsid, this) { Dock = DockStyle.Fill };
        ((ISupportInitialize)_rdp).BeginInit();
        Controls.Add(_rdp);
        Controls.Add(bar);
        ((ISupportInitialize)_rdp).EndInit();
    }

    public TransferSession Session { get; }

    public Task ClosedTask { get; }

    public RdpTransferSessionState State { get; private set; } = RdpTransferSessionState.Connecting;

    public string? Error { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            Configure();
            Connect();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            SetState(RdpTransferSessionState.Disconnected, $"Cannot connect: {ex.Message}");
        }
    }

    /// <summary>Sets up the control: the entry's address and account, every drive and the clipboard shared.</summary>
    private void Configure()
    {
        var rdp = _rdp.Control;
        rdp.Server = _settings.Host;
        if (_settings.UserName is { } user) rdp.UserName = user;
        if (_settings.Domain is { } domain) rdp.Domain = domain;
        rdp.DesktopWidth = Math.Clamp(_rdp.Width, 640, 4096);
        rdp.DesktopHeight = Math.Clamp(_rdp.Height, 480, 2048);

        var advanced = Advanced(rdp);
        advanced.RDPPort = _settings.Port;
        // Drives can only be shared all or nothing here; the window is disconnected when the copy is done (ADR-0042).
        advanced.RedirectDrives = true;
        // The start command reaches the session's Run box through the clipboard.
        Try(() => advanced.RedirectClipboard = true);
        Try(() => advanced.EnableCredSspSupport = true);
        Try(() => advanced.AuthenticationLevel = (uint)_settings.AuthenticationLevel);
        Try(() => advanced.EnableAutoReconnect = true);
        Try(() => advanced.SmartSizing = true);
        Try(() => advanced.ConnectToAdministerServer = _settings.AdministrativeSession);

        // Windows key combinations go to the session while the control has the focus: Win+R opens its Run box.
        Try(() => rdp.SecuredSettings2.KeyboardHookMode = 1);
    }

    /// <summary>The newest advanced settings the control offers (each version adds settings to the one before).</summary>
    private static dynamic Advanced(dynamic rdp)
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

    private static void Try(Action action)
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

    private void Connect()
    {
        _reconnect.Visible = false;
        Error = null;
        SetState(RdpTransferSessionState.Connecting, $"Connecting to {_settings.Host}…");
        var rdp = _rdp.Control;
        if (!string.IsNullOrEmpty(_password))
        {
            // In memory only, never in a file or on a command line (NFR-12). Set for every attempt: Reconnect needs it too.
            Advanced(rdp).ClearTextPassword = _password;
        }

        rdp.Connect();
        if (!_rdp.HasEvents)
        {
            // No events from this control: the user starts the copy once the desktop shows.
            SetState(RdpTransferSessionState.NeedsClick, "When the desktop shows, click Start copy.");
        }
    }

    public void OnControlConnecting() => SetState(RdpTransferSessionState.Connecting, $"Connecting to {_settings.Host}…");

    public void OnControlLoginComplete()
    {
        SetState(RdpTransferSessionState.Starting, "Signed in. Starting the copy in the session…");
        // Give a new session's desktop time to come up: the Run box needs it.
        After(SettleAfterLogin, () => StartCopy(clicked: false));
    }

    public void OnControlDisconnected(int reason)
    {
        _timer.Stop();
        _next = null;
        if (_closing) return;
        Error = reason switch
        {
            1 or 2 or 3 => null, // closed here, signed out or disconnected in the session, or by the server
            < 0 => $"The Remote Desktop control failed (error {-reason}).",
            _ => Describe(reason),
        };
        _reconnect.Visible = true;
        SetState(RdpTransferSessionState.Disconnected, Error is null
            ? "Disconnected. A copy that was running waits in the session; Reconnect to let it continue."
            : $"Disconnected: {Error}");
    }

    private string Describe(int reason)
    {
        try
        {
            var rdp = _rdp.Control;
            string text = rdp.GetErrorDescription((uint)reason, (uint)rdp.ExtendedDisconnectReason);
            return string.IsNullOrWhiteSpace(text) ? $"reason {reason}" : text;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidOperationException)
        {
            return $"reason {reason}";
        }
    }

    /// <summary>
    /// Types Win+R, then Ctrl+V and Enter, with the start command on the clipboard (shared into the session). Only while
    /// this window is in front: otherwise it waits for the user to click Start copy.
    /// </summary>
    private void StartCopy(bool clicked)
    {
        if (!clicked && !_request.ShouldStart())
        {
            SetState(RdpTransferSessionState.Started, "Signed in. The copy is already running in the session.");
            return;
        }

        try
        {
            Clipboard.SetDataObject(_request.StartCommand, copy: true);
        }
        catch (ExternalException)
        {
        }

        if (!clicked) Activate();
        if (!InFront())
        {
            SetState(RdpTransferSessionState.NeedsClick, "Click Start copy to start the transfer in the session.");
            return;
        }

        SetState(RdpTransferSessionState.Starting, "Starting the copy in the session…");
        _request.ResetStarted?.Invoke(); // SPEC FR-66: what follows must prove the command ran in this session
        _rdp.Focus();
        // Win+R opens the session's Run box; after it shows, Ctrl+V pastes the start command and Enter runs it.
        KeyboardInput.Chord(KeyboardInput.VkLeftWindows, KeyboardInput.VkR);
        After(RunBoxDelay, () =>
        {
            if (!InFront())
            {
                SetState(RdpTransferSessionState.NeedsClick, "The window lost the focus. Click Start copy to try again.");
                return;
            }

            _rdp.Focus();
            KeyboardInput.Chord(KeyboardInput.VkControl, KeyboardInput.VkV);
            KeyboardInput.Press(KeyboardInput.VkReturn);
            if (_request.HasStarted is null)
            {
                SetState(RdpTransferSessionState.Started,
                    "Copy started. If no progress shows in Harbor, click Start copy, or press Win+R, Ctrl+V and Enter in the session.");
                return;
            }

            SetState(RdpTransferSessionState.Checking, "Checking that the copy started in the session…");
            WaitForStart(DateTime.UtcNow + StartTimeout);
        });
    }

    /// <summary>
    /// SPEC FR-66: the start command leaves a marker on this PC when it runs in the session. Without it, the keys went
    /// somewhere else – typically a Remote Desktop window open full screen inside the session, which takes Win+R to the
    /// computer it is connected to – so the window says so instead of claiming the copy started. Nothing is typed again.
    /// </summary>
    private void WaitForStart(DateTime until)
    {
        if (Started())
        {
            SetState(RdpTransferSessionState.Started, "Copy started in the session.");
            return;
        }

        if (DateTime.UtcNow < until)
        {
            After(CheckInterval, () => WaitForStart(until));
            return;
        }

        SetState(RdpTransferSessionState.NeedsClick,
            "The copy did not start in this session. If a Remote Desktop window is open full screen in it, the keys went " +
            "to it: minimize it (or leave full screen), then click Start copy.");
    }

    private bool Started()
    {
        try
        {
            return _request.HasStarted?.Invoke() != false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True while this window is the one Windows sends keys to: only then is anything typed.</summary>
    private bool InFront() => KeyboardInput.GetForegroundWindow() == Handle;

    private void After(TimeSpan delay, Action action)
    {
        _timer.Stop();
        _next = action;
        _timer.Interval = (int)delay.TotalMilliseconds;
        _timer.Start();
    }

    private void SetState(RdpTransferSessionState state, string text)
    {
        State = state;
        _status.Text = text;
        _start.Enabled = state is not RdpTransferSessionState.Connecting and not RdpTransferSessionState.Disconnected;
        Session.Raise();
    }

    /// <summary>Disconnects (the session and its copy stay on the server) and closes.</summary>
    public void CloseFromApp()
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke(Close);
        }
        catch (InvalidOperationException)
        {
            // already closing
        }
    }

    public void ShowFromApp()
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _timer.Stop();
        try
        {
            if ((int)_rdp.Control.Connected != 0) _rdp.Control.Disconnect();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidOperationException)
        {
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        State = RdpTransferSessionState.Closed;
        Session.Raise();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _rdp.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>What the app holds of the transfer window; safe to call from any thread.</summary>
[SupportedOSPlatform("windows")]
internal sealed class TransferSession(TransferSessionForm form) : IRdpTransferSession
{
    public RdpTransferSessionState State => form.State;
    public string? Error => form.Error;
    public event EventHandler? StateChanged;

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => form.ClosedTask.WaitAsync(cancellationToken);
    public void Show() => form.ShowFromApp();
    public void Close() => form.CloseFromApp();
    internal void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
}
