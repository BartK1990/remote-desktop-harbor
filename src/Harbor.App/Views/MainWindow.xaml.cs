using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.Input;
using Harbor.App.Infrastructure;
using Harbor.App.ViewModels;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Harbor.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private bool _disposed;
    private bool _closing;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Title = AppInfo.Title;
        FocusSearchCommand = new RelayCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); });
        Loaded += OnLoaded;
    }

    public IRelayCommand FocusSearchCommand { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.Follow(this); // SPEC FR-08: "System" follows Windows light/dark changes while the app runs.
        var handle = new WindowInteropHelper(this).Handle;
        await _viewModel.InitializeAsync(() => handle, PromptPassphrase);
        SearchBox.Focus();
    }

    /// <summary>Called from a background thread while the shared vault key is unwrapped.</summary>
    private string? PromptPassphrase(bool isNewVault) =>
        Dispatcher.Invoke(() =>
        {
            var dialog = new PassphraseWindow(isNewVault) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Passphrase : null;
        });

    private void EntryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _viewModel.SelectFromTree(e.NewValue as TreeNodeViewModel);

    // Keeps a row selected from code (e.g. after saving) in view.
    private void EntryTree_ItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item) item.BringIntoView();
    }

    private void EntryTree_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking a folder toggles it (built into TreeViewItem); an entry or account row connects. Handled in the
        // preview phase so an entry with account rows connects instead of expanding (SPEC FR-09).
        // A quick double click on the expand arrow only toggles the row.
        if (RowAt(e.OriginalSource, out var onExpander)?.DataContext is EntryViewModel or AccountViewModel && !onExpander
            && _viewModel.ConnectCommand.CanExecute(null))
        {
            _viewModel.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void EntryTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_viewModel.SelectedNode is FolderViewModel folder)
        {
            folder.IsExpanded = !folder.IsExpanded;
            e.Handled = true;
        }
        else if (_viewModel.ConnectCommand.CanExecute(null))
        {
            _viewModel.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Right-click selects the row under the mouse, so the context menu acts on it.
    private void EntryTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.OriginalSource) is { } row)
        {
            row.Focus();
            row.IsSelected = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down) return;
        // Down moves from the search box into the tree, onto the selected row or the first one.
        var row = _viewModel.SelectedNode is { } node && EntryTree.ItemContainerGenerator.ContainerFromItem(node) is TreeViewItem selected
            ? selected
            : EntryTree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
        if (row is null) return;
        row.Focus();
        row.IsSelected = true;
        e.Handled = true;
    }

    private static TreeViewItem? RowAt(object source) => RowAt(source, out _);

    private static TreeViewItem? RowAt(object source, out bool onExpander)
    {
        onExpander = false;
        for (var d = source as DependencyObject; d is not null && d is not TreeView;
             d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is TreeViewItem item) return item;
            if (d is System.Windows.Controls.Primitives.ToggleButton) onExpander = true;
        }

        return null;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_disposed) return;

        // The confirmation is asynchronous, so cancel now and close again once it is answered and the app has shut down.
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            // Presence is published by this app; closing it ends the "in use" records (SPEC FR-34).
            if (_viewModel.ActiveLaunchCount > 0 && !await DialogService.ConfirmAsync(this, "Remote Desktop Harbor",
                    $"{_viewModel.ActiveLaunchCount} Remote Desktop session(s) started from this app are still open. " +
                    "After closing, other users will no longer see them as in use. Close anyway?"))
            {
                return;
            }

            await _viewModel.DisposeAsync();
            _disposed = true;
            // DisposeAsync often completes synchronously, so we may still be inside this Closing event, where Close()
            // throws "Cannot ... Close ... while a Window is closing". Close again once the event has finished.
            _ = Dispatcher.BeginInvoke(Close);
        }
        finally
        {
            _closing = false;
        }
    }
}
