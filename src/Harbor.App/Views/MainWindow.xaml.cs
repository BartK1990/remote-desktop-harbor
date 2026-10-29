using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.Input;
using Harbor.App.ViewModels;

namespace Harbor.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _disposed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Title = Infrastructure.AppInfo.Title;
        FocusSearchCommand = new RelayCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); });
        Loaded += OnLoaded;
    }

    public IRelayCommand FocusSearchCommand { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
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

    private void EntryTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking a folder toggles it (built into TreeViewItem); only an entry row connects.
        if (RowAt(e.OriginalSource)?.DataContext is EntryViewModel && _viewModel.ConnectCommand.CanExecute(null))
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

    private static TreeViewItem? RowAt(object source)
    {
        for (var d = source as DependencyObject; d is not null && d is not TreeView;
             d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is TreeViewItem item) return item;
        }

        return null;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_disposed) return;

        // Presence is published by this app; closing it ends the "in use" records (SPEC FR-34).
        if (_viewModel.ActiveLaunchCount > 0 && MessageBox.Show(this,
                $"{_viewModel.ActiveLaunchCount} Remote Desktop session(s) started from this app are still open. " +
                "After closing, other users will no longer see them as in use. Close anyway?",
                "Remote Desktop Harbor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        await _viewModel.DisposeAsync();
        _disposed = true;
        // DisposeAsync often completes synchronously, so we may still be inside this Closing event, where Close()
        // throws "Cannot ... Close ... while a Window is closing". Close again once the event has finished.
        _ = Dispatcher.BeginInvoke(Close);
    }
}
