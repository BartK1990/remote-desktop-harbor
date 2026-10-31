using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Harbor.App.Infrastructure;
using Harbor.Core;
using Harbor.Core.Security;
using Harbor.Data.Providers;
using Microsoft.Data.SqlClient;
using static Harbor.Screenshots.Native;

namespace Harbor.Screenshots;

/// <summary>
/// Captures the documentation screenshots from the real app (ADR-0031): seeds a demo shared database on SQL Server
/// LocalDB through the app's own workspace, starts RemoteDesktopHarbor.exe in portable mode next to this tool,
/// drives it with the keyboard and copies each window's pixels to a PNG.
/// Usage: Harbor.Screenshots.exe &lt;output folder&gt;   (Windows only; needs SQL Server LocalDB)
/// </summary>
internal static class Program
{
    private const string Database = "HarborDemo";
    private static readonly string Server = Environment.GetEnvironmentVariable("HARBOR_DEMO_SERVER") ?? @"(localdb)\MSSQLLocalDB";
    private static readonly string ConnectionString =
        $"Server={Server};Database={Database};Integrated Security=true;Encrypt=false;TrustServerCertificate=true";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: Harbor.Screenshots <output folder>");
            return 2;
        }

        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);

        // Portable mode: the app and this tool both keep their data in .\data next to them (ADR-0003), away from the
        // user's real data. Must exist before AppPaths is first used.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "portable.txt"), "");
        var exe = Path.Combine(AppContext.BaseDirectory, "RemoteDesktopHarbor.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Build the app into this folder first.", exe);
        if (Directory.Exists(AppPaths.DataDirectory)) Directory.Delete(AppPaths.DataDirectory, recursive: true);

        Step("Creating the demo database");
        await ResetDatabaseAsync();
        var settings = new HarborSettings
        {
            StorageMode = StorageMode.Remote,
            RemoteProvider = SqlServerProvider.ProviderName,
            RemoteKeyScheme = SignInKeyProtector.SchemeName,
            RemoteAutoMigrate = true,
            AuthMode = AuthMode.Anonymous,
            AnonymousName = "Alex Kim",
            Theme = AppTheme.Light,
            CollapsedFolders = ["Lab"],
        };
        SettingsStore.SetRemoteConnectionString(settings, ConnectionString);
        SettingsStore.Save(settings);

        Step("Seeding the catalog");
        using (var host = await AppHost.StartAsync(settings, () => 0, _ => null))
        {
            var entries = await DemoCatalog.SeedAsync(host);
            await DemoCatalog.AddColleaguesAsync(ConnectionString, entries);
            settings.PinnedEntryIds = [entries["APP02 – Reporting"].Id];
            settings.RecentEntryIds = [entries["APP02 – Reporting"].Id, entries["SQL01"].Id];
        }

        SettingsStore.Save(settings);

        Step("Starting the app");
        using var app = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = AppContext.BaseDirectory })!;
        try
        {
            await CaptureAllAsync(app, output);
        }
        finally
        {
            if (!app.HasExited) app.Kill(entireProcessTree: true);
        }

        Step($"Done: {output}");
        return 0;
    }

    private static async Task CaptureAllAsync(Process app, string output)
    {
        var main = await WaitForMainWindowAsync(app);
        // Hosted Windows runners have a 1024 × 768 screen: use 1100 × 720 where it fits, else the work area.
        var area = System.Windows.SystemParameters.WorkArea;
        SetWindowPos(main, 0, (int)area.Left + 8, (int)area.Top + 8, (int)Math.Min(1100, area.Width - 16), (int)Math.Min(720, area.Height - 16),
            SwpNoZOrder);
        await Task.Delay(6000); // sign-in, sync and presence

        // Main window with APP02 selected (quick connect, Shift+Enter only selects).
        Focus(main);
        await QuickSelectAsync("APP02");
        await CaptureAsync(main, output, "main");

        // Quick connect palette.
        Press(VkControl, 'K');
        await Task.Delay(400);
        Type("app0");
        await Task.Delay(800);
        await CaptureAsync(main, output, "quick-connect");
        Press(VkEscape);
        await Task.Delay(400);

        // Connecting to APP02 as svc-admin would take over Jane's session: the app's confirmation (FR-33, FR-53).
        await QuickSelectAsync("APP02");
        Press(VkReturn);
        await CaptureDialogAsync(app, output, "inuse");
        Press(VkEscape); // Cancel is the default of a risky question

        // Entry editor: General, Accounts, Display.
        await QuickSelectAsync("line of business");
        Press(VkF2);
        var editor = await CaptureDialogAsync(app, output, "editor");
        Press(VkControl, VkTab);
        await CaptureAsync(editor, output, "editor-accounts");
        Press(VkControl, VkTab);
        await CaptureAsync(editor, output, "editor-display");
        Press(VkEscape);
        await Task.Delay(600);

        // Settings: Storage, Shared vault, Sign-in, (Remote Desktop), Appearance.
        Focus(main);
        Press(VkControl, VkOemComma);
        var settings = await CaptureDialogAsync(app, output, "settings");
        Press(VkControl, VkTab);
        await CaptureAsync(settings, output, "settings-vault");
        Press(VkControl, VkTab);
        await CaptureAsync(settings, output, "settings-signin");
        Press(VkControl, VkTab);
        Press(VkControl, VkTab);
        await CaptureAsync(settings, output, "settings-appearance");
        Press(VkEscape);
        await Task.Delay(600);

        // Offline: take the demo database offline, refresh, search "sql" (FR-22). (Stopping LocalDB would not do:
        // it starts again on the next connection attempt.)
        await SetDatabaseOfflineAsync();
        Focus(main);
        Press(VkF5);
        await Task.Delay(8000);
        Press(VkControl, 'F');
        Type("sql");
        await Task.Delay(500);
        await QuickSelectAsync("SQL01");
        await CaptureAsync(main, output, "offline");
    }

    private static async Task QuickSelectAsync(string query)
    {
        Press(VkControl, 'K');
        await Task.Delay(400);
        Type(query);
        await Task.Delay(500);
        Press(VkShift, VkReturn);
        await Task.Delay(900);
    }

    private static async Task<nint> WaitForMainWindowAsync(Process app)
    {
        for (var i = 0; i < 120; i++)
        {
            app.Refresh();
            if (app.MainWindowHandle != 0) return app.MainWindowHandle;
            await Task.Delay(500);
        }

        throw new TimeoutException("The app's main window did not appear.");
    }

    /// <summary>Waits for a window of the app (other than the main one) to come to the front and captures it.</summary>
    private static async Task<nint> CaptureDialogAsync(Process app, string output, string name)
    {
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out var pid);
            app.Refresh();
            if (pid == app.Id && hwnd != app.MainWindowHandle)
            {
                await CaptureAsync(hwnd, output, name);
                return hwnd;
            }
        }

        throw new TimeoutException($"No dialog appeared for '{name}'.");
    }

    // Windows only lets a process take the foreground while a key event is in flight; a lone Alt press provides one.
    private static void Focus(nint hwnd)
    {
        Press(VkMenu);
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);
    }

    /// <summary>Copies the window's visible frame from the screen (it is in front) to <c>&lt;name&gt;.png</c>.</summary>
    private static async Task CaptureAsync(nint hwnd, string output, string name)
    {
        await Task.Delay(700); // let animations settle
        if (DwmGetWindowAttribute(hwnd, DwmExtendedFrameBounds, out var r, Marshal.SizeOf<Rect>()) != 0)
        {
            throw new InvalidOperationException($"Cannot read the bounds of the window for '{name}'.");
        }

        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, width, height, screen, r.Left, r.Top, SrcCopy);
            SelectObject(memory, previous);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            await using var file = File.Create(Path.Combine(output, $"{name}.png"));
            encoder.Save(file);
            Step($"Captured {name}.png ({width} × {height})");
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    private static async Task ResetDatabaseAsync()
    {
        await RunOnMasterAsync(
            $"IF DB_ID('{Database}') IS NOT NULL BEGIN ALTER DATABASE [{Database}] SET ONLINE; ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}]; END");
        var provider = (IServerDatabaseProvider)DatabaseProviders.Get(SqlServerProvider.ProviderName);
        await provider.EnsureDatabaseExistsAsync(ConnectionString);
    }

    private static async Task RunOnMasterAsync(string sql)
    {
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetDatabaseOfflineAsync()
    {
        await RunOnMasterAsync($"ALTER DATABASE [{Database}] SET OFFLINE WITH ROLLBACK IMMEDIATE");
        Step("Demo database offline (offline shot)");
    }

    private static void Step(string text) => Console.WriteLine($"[screenshots] {text}");
}
