using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.ConnectCommand.CanExecute(null)) _viewModel.ConnectCommand.Execute(null);
    }

    private void EntryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.ConnectCommand.CanExecute(null))
        {
            _viewModel.ConnectCommand.Execute(null);
            e.Handled = true;
        }
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
        Close();
    }
}
