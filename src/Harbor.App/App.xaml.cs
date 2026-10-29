using System.Windows;
using System.Windows.Threading;
using Harbor.App.Infrastructure;
using Harbor.App.ViewModels;
using Harbor.App.Views;

namespace Harbor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Show the window first and do sign-in / database work afterwards: fast perceived startup (SPEC NFR-01).
        var settings = SettingsStore.Load();
        ThemeService.Apply(settings.Theme); // SPEC FR-08: before the first window, so it never flashes the wrong theme.
        var dialogs = new DialogService();
        var viewModel = new MainViewModel(settings, dialogs);
        var window = new MainWindow(viewModel);
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);
        MessageBox.Show(e.Exception.Message, "Remote Desktop Harbor", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
