# Harbor.Screenshots

Captures the documentation screenshots (`website/src/assets/screenshots/*.png`) from the **real app** on Windows.
See [ADR-0031](../../docs/adr/0031-screenshots-from-ci.md).

What it does:

1. Writes `portable.txt` next to itself, so the app and the tool keep their data in `.\data` here — never in your
   real `%LOCALAPPDATA%\RemoteDesktopHarbor`.
2. Creates the database `HarborDemo` on SQL Server LocalDB (dropped first if it exists) and writes settings for a
   shared database with the *Not protected* vault key, sign-in *No sign-in* as "Alex Kim", light theme.
3. Saves the fictional contoso.local catalog through the app's own workspace (`DemoCatalog`), and two presence
   records of colleagues (Jane Doe on APP02, Sam Patel on APP01 as ops).
4. Starts `RemoteDesktopHarbor.exe` from the same folder and drives it with the keyboard (quick connect, F2,
   Ctrl+,, Ctrl+Tab, …), copying each window to a PNG. For the offline shot it takes the demo database offline.

Run locally (Windows with SQL Server LocalDB; it takes over the keyboard for about a minute):

```powershell
dotnet build tools/Harbor.Screenshots -c Release -o out/shots
out/shots/Harbor.Screenshots.exe website/src/assets/screenshots
```

`HARBOR_DEMO_SERVER` overrides the server (default `(localdb)\MSSQLLocalDB`). In CI the
[Screenshots workflow](../../.github/workflows/screenshots.yml) runs it on every pull request that touches the app
(artifact *screenshots*); run the workflow by hand with **commit** to update the images in the docs.

`passphrase.png` is still rendered from the HTML mockup (`website/mockups`): it needs a passphrase-protected vault.
