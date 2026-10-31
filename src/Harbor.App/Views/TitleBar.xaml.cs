using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Harbor.Windows.Interop;

namespace Harbor.App.Views;

/// <summary>
/// The app's own title bar (SPEC FR-52, ADR-0029). Put it in the top 32 px of a window whose <c>WindowChrome</c> has
/// <c>CaptionHeight="32"</c>; the window keeps the system's dragging, double-click maximize, snapping, resizing,
/// shadow and system menu (Alt+Space, right-click).
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty CenterContentProperty =
        DependencyProperty.Register(nameof(CenterContent), typeof(object), typeof(TitleBar));

    public static readonly DependencyProperty RightContentProperty =
        DependencyProperty.Register(nameof(RightContent), typeof(object), typeof(TitleBar));

    public static readonly DependencyProperty ShowMinimizeMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMinimizeMaximize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, (d, _) => ((TitleBar)d).UpdateButtons()));

    private Window? _window;
    private HwndSource? _source;
    private Thickness _contentMargin;
    private bool _maximizeHot;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            if (_window is not null) _window.StateChanged -= OnStateChanged;
            _source?.RemoveHook(WndProc);
        };
    }

    /// <summary>Shown in the middle of the bar (e.g. the quick connect field); stays clickable.</summary>
    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    /// <summary>Shown left of the caption buttons (e.g. the signed-in user); stays clickable.</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>Dialogs show only Close.</summary>
    public bool ShowMinimizeMaximize
    {
        get => (bool)GetValue(ShowMinimizeMaximizeProperty);
        set => SetValue(ShowMinimizeMaximizeProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null || Window.GetWindow(this) is not { } window) return;
        _window = window;
        _contentMargin = (window.Content as FrameworkElement)?.Margin ?? default;
        window.StateChanged += OnStateChanged;
        OnStateChanged(window, EventArgs.Empty);

        // Added after WindowChrome's hook, so it runs first (HwndSource calls the newest hook first).
        _source = PresentationSource.FromVisual(window) as HwndSource;
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Windows 11 snap layouts (SPEC FR-52, ADR-0029): Windows shows the layout flyout only for a window part that hit
    /// tests as <c>HTMAXBUTTON</c>. The mouse over our Maximize button is then "non-client", so the button's hover,
    /// press and click come from non-client messages instead of WPF's mouse events.
    /// </summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WindowInterop.WmNcHitTest:
                var over = IsOverMaximize(lParam);
                SetMaximizeState(over ? "Hover" : null, hwnd);
                if (!over) return 0;
                handled = true;
                return WindowInterop.HitTestMaxButton;
            case WindowInterop.WmNcLButtonDown or WindowInterop.WmNcLButtonDblClk when wParam == WindowInterop.HitTestMaxButton:
                SetMaximizeState("Pressed", hwnd);
                handled = true; // no system-drawn button press
                break;
            case WindowInterop.WmNcLButtonUp when wParam == WindowInterop.HitTestMaxButton:
                SetMaximizeState("Hover", hwnd);
                handled = true;
                Maximize_Click(MaximizeButton, new RoutedEventArgs());
                break;
            case WindowInterop.WmNcMouseLeave:
                SetMaximizeState(null, hwnd);
                break;
        }

        return 0;
    }

    private bool IsOverMaximize(nint lParam)
    {
        if (MaximizeButton.Visibility != Visibility.Visible || !MaximizeButton.IsLoaded) return false;
        var (x, y) = WindowInterop.ScreenPoint(lParam);
        var p = MaximizeButton.PointFromScreen(new Point(x, y));
        return p.X >= 0 && p.Y >= 0 && p.X < MaximizeButton.ActualWidth && p.Y < MaximizeButton.ActualHeight;
    }

    // The caption button template shows Tag "Hover" / "Pressed" like IsMouseOver / IsPressed.
    private void SetMaximizeState(string? state, nint hwnd)
    {
        if (Equals(MaximizeButton.Tag, state)) return;
        MaximizeButton.Tag = state;
        var hot = state is not null;
        if (hot && !_maximizeHot) WindowInterop.TrackNonClientMouseLeave(hwnd);
        _maximizeHot = hot;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window is null) return;
        var maximized = _window.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");

        // A maximized window with a custom frame extends past the screen by the resize border; keep the content on screen.
        if (_window.Content is FrameworkElement root)
        {
            var b = SystemParameters.WindowResizeBorderThickness;
            root.Margin = maximized
                ? new Thickness(_contentMargin.Left + b.Left, _contentMargin.Top + b.Top, _contentMargin.Right + b.Right, _contentMargin.Bottom + b.Bottom)
                : _contentMargin;
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var visible = ShowMinimizeMaximize && _window?.ResizeMode is not ResizeMode.NoResize;
        MinimizeButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MaximizeButton.Visibility = visible && _window?.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.MinimizeWindow(_window);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        if (_window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => _window?.Close();
}
