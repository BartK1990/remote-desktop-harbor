using System.Windows;
using System.Windows.Controls;

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
    private Thickness _contentMargin;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { if (_window is not null) _window.StateChanged -= OnStateChanged; };
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
