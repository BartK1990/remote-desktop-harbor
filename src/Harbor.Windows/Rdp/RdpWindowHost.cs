using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Harbor.Core.Rdp;

namespace Harbor.Windows.Rdp;

/// <summary>
/// Harbor's own Remote Desktop window for ordinary connections (SPEC FR-67, ADR-0045): the Remote Desktop ActiveX control
/// of Windows in a normal window that can be resized and maximized without going full screen. While signed in, the remote
/// desktop is resized to the window (as mstsc's dynamic resolution does), so a maximized window gets a desktop of exactly
/// its size. Runs on its own UI thread like the transfer window (FR-65, FR-66).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RdpWindowHost : IRdpWindowHost
{
    private readonly Lazy<string?> _clsid = new(RdpControl.FindClass);

    public bool IsAvailable => _clsid.Value is not null;

    public IRdpSession Open(RdpWindowSettings settings, string? password)
    {
        var clsid = _clsid.Value ?? throw new InvalidOperationException("The Remote Desktop control is not installed on this PC.");
        return RdpControl.OpenWindow("Remote Desktop window",
            closed => new RdpWindowForm(clsid, settings, password, closed), form => form.Session);
    }
}

/// <summary>The connection window: the remote desktop, with a status bar while connecting or disconnected.</summary>
[SupportedOSPlatform("windows")]
internal sealed class RdpWindowForm : Form, IRdpControlEvents
{
    // Resizing a session is not free (the server redraws the desktop): wait until the user stops dragging the border.
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private const int MaxResizeFailures = 4;
    private static readonly Size StartSize = new(1280, 800);

    private readonly RdpWindowSettings _settings;
    private readonly string? _password;
    private readonly RdpControlHost _rdp;
    private readonly Panel _bar = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 6, 10, 6) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _reconnect = new() { Text = "Reconnect", AutoSize = true, Dock = DockStyle.Right, Visible = false };
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = (int)ResizeDelay.TotalMilliseconds };
    private bool _signedIn;
    private bool _canResizeSession = true;
    private int _resizeFailures;
    private bool _forceResize;
    private Size _sessionSize;
    private int _sessionDpi;
    private bool _closing;

    public RdpWindowForm(string clsid, RdpWindowSettings settings, string? password, Task closed)
    {
        _settings = settings;
        _password = password;
        Session = new RdpWindowSession(closed);

        Text = $"{settings.Title} - Remote Desktop Harbor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 360);
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = InitialSize(settings);

        _bar.Controls.Add(_status);
        _bar.Controls.Add(_reconnect);
        _reconnect.Click += (_, _) => Connect();
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            _resizeTimer.Interval = (int)ResizeDelay.TotalMilliseconds;
            ResizeSession(force: _forceResize);
        };

        _rdp = new RdpControlHost(clsid, this) { Dock = DockStyle.Fill };
        ((ISupportInitialize)_rdp).BeginInit();
        Controls.Add(_rdp);
        Controls.Add(_bar);
        ((ISupportInitialize)_rdp).EndInit();
    }

    public RdpWindowSession Session { get; }

    /// <summary>The entry's fixed size when it has one, else a large window; never larger than the screen's work area.</summary>
    private static Size InitialSize(RdpWindowSettings settings)
    {
        var area = Screen.PrimaryScreen?.WorkingArea.Size ?? StartSize;
        var wanted = settings.Resolution is { } r ? new Size(r.Width, r.Height) : StartSize;
        return new Size(Math.Min(wanted.Width, area.Width * 9 / 10), Math.Min(wanted.Height, area.Height * 9 / 10));
    }

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
            ShowBar($"Cannot connect: {ex.Message}", reconnect: true);
        }
    }

    /// <summary>The entry's address, account and redirections; the window's own size (SPEC FR-67).</summary>
    private void Configure()
    {
        var rdp = _rdp.Control;
        rdp.Server = _settings.Host;
        if (_settings.UserName is { } user) rdp.UserName = user;
        if (_settings.Domain is { } domain) rdp.Domain = domain;
        RdpControl.Try(() => rdp.ColorDepth = _settings.ColorDepth);
        RdpControl.Try(() => rdp.FullScreen = false);

        var advanced = RdpControl.Advanced(rdp);
        advanced.RDPPort = _settings.Port;
        RdpControl.Try(() => advanced.RedirectClipboard = _settings.RedirectClipboard);
        RdpControl.Try(() => advanced.RedirectPrinters = _settings.RedirectPrinters);
        RdpControl.Try(() => advanced.RedirectSmartCards = _settings.RedirectSmartCards);
        RdpControl.Try(() => advanced.RedirectDrives = _settings.RedirectDrives);
        RdpControl.Try(() => advanced.EnableCredSspSupport = true);
        RdpControl.Try(() => advanced.AuthenticationLevel = (uint)_settings.AuthenticationLevel);
        RdpControl.Try(() => advanced.EnableAutoReconnect = _settings.AutoReconnect);
        RdpControl.Try(() => advanced.ConnectToAdministerServer = _settings.AdministrativeSession);
        RdpControl.Try(() => advanced.AudioCaptureRedirectionMode = _settings.RecordAudio);
        RdpControl.Try(() => advanced.SmartSizing = _settings.SmartSizing);
        RdpControl.Try(() => rdp.SecuredSettings2.KeyboardHookMode = _settings.KeyboardHook);
        RdpControl.Try(() => rdp.SecuredSettings2.AudioRedirectionMode = _settings.Audio);
    }

    private void Connect()
    {
        _reconnect.Visible = false;
        _signedIn = false;
        ShowBar($"Connecting to {_settings.Host}…", reconnect: false);
        var rdp = _rdp.Control;

        // The desktop starts at the window's size (or the entry's fixed size) and follows the window once signed in.
        var size = _settings.DynamicResolution || _settings.Resolution is null
            ? RdpWindowSettings.DesktopSizeFor(_rdp.ClientSize.Width, _rdp.ClientSize.Height + _bar.Height)
            : (_settings.Resolution.Width, _settings.Resolution.Height);
        rdp.DesktopWidth = size.Width;
        rdp.DesktopHeight = size.Height;
        _sessionSize = new Size(size.Width, size.Height);
        _sessionDpi = 0;
        _resizeFailures = 0;

        if (!string.IsNullOrEmpty(_password))
        {
            // In memory only, never in a file or on a command line (NFR-12). Set for every attempt: Reconnect needs it too.
            RdpControl.Advanced(rdp).ClearTextPassword = _password;
        }

        rdp.Connect();
        if (!_rdp.HasEvents) HideBar(); // no events from this control: show the desktop as it comes
    }

    public void OnControlConnecting() => ShowBar($"Connecting to {_settings.Host}…", reconnect: false);

    public void OnControlLoginComplete()
    {
        _signedIn = true;
        HideBar();
        _rdp.Focus();
        // Hiding the bar gave the desktop more room, and the server now knows this PC's scale: fit the session once.
        ResizeSession(force: true);
    }

    public void OnControlDisconnected(int reason)
    {
        _resizeTimer.Stop();
        _signedIn = false;
        if (_closing) return;

        // 1 = closed here, 2 = signed out or disconnected in the session: the window closes, as mstsc's does.
        if (reason is 1 or 2)
        {
            BeginInvoke(Close);
            return;
        }

        var error = reason < 0 ? $"The Remote Desktop control failed (error {-reason})." : RdpControl.Describe(_rdp, reason);
        ShowBar($"Disconnected: {error}", reconnect: true);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_signedIn) _rdp.Focus(); // keys typed in the window go to the session
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_signedIn || WindowState == FormWindowState.Minimized) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (_signedIn) ResizeSession(force: true); // moved to a screen with another scale
    }

    /// <summary>
    /// Gives the session a desktop of the window's size (SPEC FR-67). Needs a server that supports resizing (Windows 8.1 /
    /// Server 2012 R2 and later); otherwise the picture is scaled to the window instead (smart sizing) and the desktop
    /// keeps the size it connected with. An entry with dynamic resolution off keeps its size and its own smart sizing.
    /// </summary>
    private void ResizeSession(bool force)
    {
        if (!_signedIn || WindowState == FormWindowState.Minimized || _rdp.ClientSize.Width <= 0) return;
        if (!_settings.DynamicResolution || !_canResizeSession)
        {
            ScaleToWindow();
            return;
        }

        var (width, height) = RdpWindowSettings.DesktopSizeFor(_rdp.ClientSize.Width, _rdp.ClientSize.Height);
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        if (!force && _sessionSize == new Size(width, height) && _sessionDpi == dpi) return;

        try
        {
            // Physical size in millimetres and the scale of this screen, so text keeps the size it has on this PC.
            var desktopScale = (uint)Math.Clamp(dpi * 100 / 96, 100, 500);
            var deviceScale = desktopScale < 120 ? 100u : desktopScale < 160 ? 140u : 180u;
            _rdp.Control.UpdateSessionDisplaySettings((uint)width, (uint)height, (uint)(width * 254 / (dpi * 10)),
                (uint)(height * 254 / (dpi * 10)), 0u, desktopScale, deviceScale);
            _sessionSize = new Size(width, height);
            _sessionDpi = dpi;
            _resizeFailures = 0;
            _forceResize = false;
        }
        catch (COMException) when (++_resizeFailures < MaxResizeFailures)
        {
            // Right after sign-in the session may not take a new size yet: try again shortly.
            _forceResize = true;
            _resizeTimer.Stop();
            _resizeTimer.Interval = (int)RetryDelay.TotalMilliseconds;
            _resizeTimer.Start();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException)
        {
            // An older control or server: scale the picture instead; the next connection starts at the window's size.
            _canResizeSession = false;
            ScaleToWindow();
        }
    }

    /// <summary>Fallback when the session cannot be resized: smart sizing fits the desktop into the window.</summary>
    private void ScaleToWindow()
    {
        if (!_settings.DynamicResolution || _settings.SmartSizing) return; // the entry's own choice applies
        RdpControl.Try(() => RdpControl.Advanced(_rdp.Control).SmartSizing = true);
    }

    private void ShowBar(string text, bool reconnect)
    {
        _status.Text = text;
        _reconnect.Visible = reconnect;
        _bar.Visible = true;
    }

    private void HideBar() => _bar.Visible = false;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Disconnects; the session stays on the server, as when mstsc's window is closed.
        _closing = true;
        _resizeTimer.Stop();
        try
        {
            if ((int)_rdp.Control.Connected != 0) _rdp.Control.Disconnect();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidOperationException)
        {
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resizeTimer.Dispose();
            _rdp.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>What the app holds of the connection window: completes when it closes (presence, SPEC FR-33).</summary>
internal sealed class RdpWindowSession(Task closed) : IRdpSession
{
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => closed.WaitAsync(cancellationToken);
}
