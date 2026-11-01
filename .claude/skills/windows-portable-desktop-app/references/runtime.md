# Runtime: paths, settings, logging, startup, secrets

## Data paths and portable mode

```csharp
/// Default: %LOCALAPPDATA%\MyApp (per user, no admin, virtualised correctly under MSIX).
/// Portable: a portable.txt next to the exe puts data in .\data.
public static class AppPaths
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string DataDirectory { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LocalDatabase => Path.Combine(DataDirectory, "app.db");
    public static string TempDirectory => Path.Combine(DataDirectory, "tmp");
    public static string LogFile => Path.Combine(DataDirectory, "logs", "app.log");
}
```

- Use `AppContext.BaseDirectory`, never `Assembly.Location` (empty in single-file apps).
- `%LOCALAPPDATA%`, not `%APPDATA%`: no roaming of machine-bound (DPAPI) data, and MSIX handles it transparently.
- Portable mode does not apply under MSIX (install dir is read-only); the Store build uses the default location.
- If the app writes temp files (e.g. handing a file to another program), keep them under `TempDirectory`, delete
  them after use, and sweep leftovers at startup — a crash must not leave them forever.
- A per-remote cache file can be named from a hash of its connection identity so switching sources never mixes data.

## Settings

- One JSON file, `System.Text.Json` with a **source-generated** `JsonSerializerContext` (no reflection, faster
  start), `UseStringEnumConverter = true`, indented.
- **Load never throws**: on `IOException`/`JsonException` log and return defaults.
- **Save atomically**: write `settings.json.tmp`, then `File.Move(tmp, file, overwrite: true)`.
- Secret-bearing values (connection strings with passwords, tokens) are stored DPAPI-protected (`…Protected`
  property holding base64) — never in clear text.
- Personal state (pins, recents, collapsed folders, window size) belongs in settings, not in a shared store.

## Logging

A tiny append-only file logger is enough; it must never take the app down and never see a secret:

```csharp
public static class Log
{
    private static readonly Lock Gate = new();
    public static void Info(string m) => Write("INFO", m);
    public static void Error(string m, Exception? ex = null) =>
        Write("ERROR", ex is null ? m : $"{m}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Exists && info.Length > 2 * 1024 * 1024)
                    File.Move(AppPaths.LogFile, AppPaths.LogFile + ".1", overwrite: true); // 1-file rotation
                File.AppendAllText(AppPaths.LogFile, $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}
```

Log exception type + message, not whole objects; never log connection strings (mask passwords as `********` if one
must be displayed).

## Startup sequence (App.xaml.cs)

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    DispatcherUnhandledException += OnUnhandledException;

    var settings = SettingsStore.Load();          // small, local, synchronous
    ThemeService.Apply(settings.Theme);           // before the first window: no flash of the wrong theme
    var dialogs = new DialogService();
    var vm = new MainViewModel(settings, dialogs); // hand-written composition root, no DI container
    var window = new MainWindow(vm);
    dialogs.Owner = window;
    MainWindow = window;
    window.Show();                                 // then: vm starts sign-in / DB / sync asynchronously
}

private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
{
    Log.Error("Unhandled exception", e.Exception);
    try { MessageWindow.Show(Current?.MainWindow, "Something went wrong", e.Exception.Message, MessageKind.Error); }
    catch (Exception) { MessageBox.Show(e.Exception.Message, "MyApp", MessageBoxButton.OK, MessageBoxImage.Error); }
    e.Handled = true;
}
```

Rules: show from local data first (e.g. a local SQLite file), network after; never block the UI thread on
sign-in, sync or I/O; virtualize lists (`VirtualizingPanel.IsVirtualizing`, also `IsVirtualizingWhenGrouping`);
filter in memory for thousands of items. Don't run schema creation or similar slow work at startup unless needed.

## Version and build info

Read `AssemblyInformationalVersionAttribute` (`"1.2.3+<commit>"`): the part before `+` is the version, the rest is
the commit. Show it in the window title and an About section, so users and releases always match.

## Secrets

- **Single secret** (e.g. a connection string in settings): `ProtectedData.Protect(bytes, entropy,
  DataProtectionScope.CurrentUser)` (package `System.Security.Cryptography.ProtectedData`) in the `Windows` project.
- **Many secrets** (a vault): envelope encryption in `Core` (OS-neutral, testable on Linux):
  - one random 256-bit data key per vault; each secret encrypted with **AES-256-GCM**;
  - format `[version][keyId 16][nonce 12][tag 16][ciphertext]`; the **row id as associated data** so a ciphertext
    moved to another row fails; the key id detects a wrong key early and enables future rotation;
  - the data key is stored *wrapped* by an `IKeyProtector` named by a scheme string stored with it, e.g.
    `dpapi-user+machine` (DPAPI CurrentUser then LocalMachine: only this user on this machine — default for local),
    `dpapi-user` (roams with the profile). Shared stores need another scheme (DPAPI-NG to an AD group SID,
    passphrase with PBKDF2-SHA256 600k iterations) — document the trade-off in an ADR.
- Decrypt only at the moment of use; zero buffers where the API allows; clear a copied password from the clipboard
  after ~30 s if unchanged.
- Warn users: a Windows profile/password reset by an admin can make DPAPI keys unrecoverable.

## Handing secrets to other programs

Never via command-line arguments (visible in the process list) or files. Use Windows Credential Manager
(`CredWrite` via P/Invoke) with `CRED_PERSIST_SESSION` so it disappears at logoff even if the app crashes, start the
program, then delete the credential after a short delay. Example: Remote Desktop reads `TERMSRV/<host>`
(`CRED_TYPE_DOMAIN_PASSWORD`) and is started as `%SystemRoot%\System32\mstsc.exe <temp>.rdp` with password keys
stripped from the file. Starting other processes normally is fine for Store full-trust apps.

## Identity (if the app needs to know the user)

Windows account by default (`WindowsIdentity.GetCurrent()`, no prompt, works offline; AD look-ups with a short
timeout). Entra ID via MSAL with the **WAM broker** (`Microsoft.Identity.Client.Broker`) for silent SSO on
Entra-joined devices. Don't derive authorization to external resources from app identity — let the resource
(e.g. the database) authenticate on its own.
