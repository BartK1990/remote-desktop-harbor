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
        // SPEC NFR-14: whatever thread an exception escapes from, the log says what it was and where, before the process ends.
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Show the window first and do sign-in / database work afterwards: fast perceived startup (SPEC NFR-01).
        var settings = SettingsStore.Load();
        // SPEC FR-75: the editor's monitor picker lists this PC's monitors, numbered as Remote Desktop numbers them.
        Monitors.Current = () =>
        {
            try
            {
                return Harbor.Windows.Rdp.LocalMonitors.Current();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or EntryPointNotFoundException)
            {
                Log.Error("This PC's monitors could not be read", ex);
                return [];
            }
        };
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
        Log.Crash("Unhandled exception on the UI thread", e.Exception);
        try
        {
            MessageWindow.Show(Current?.MainWindow, "Something went wrong", e.Exception.Message, MessageKind.Error); // SPEC FR-53
        }
        catch (Exception)
        {
            // The app's own window could not be shown (e.g. resources broken); fall back to the system message box.
            MessageBox.Show(e.Exception.Message, "Remote Desktop Harbor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    // An exception left a thread other than the UI thread (or the UI thread outside the dispatcher): the runtime ends the
    // process after this, so only log it.
    private static void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) Log.Crash("Unhandled exception, the app closes", ex);
        else Log.Error($"Unhandled exception, the app closes: {e.ExceptionObject}");
    }

    // A failed task nobody awaited: log it and keep running (the runtime's default too).
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Crash("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
