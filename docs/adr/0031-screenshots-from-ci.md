# ADR-0031: Documentation screenshots captured from the real app in CI

* Status: Accepted
* Date: 2026-10-31
* Requirements: NFR-04 (Windows CI); documentation (ADR-0013)

## Context

The app is WPF and only runs on Windows, so the docs used HTML mockups that imitate each window. They drift from the
real UI with every change and cannot show the real Fluent rendering, fonts or icons.

## Options considered

1. **Keep mockups** — no Windows needed; always slightly wrong.
2. **Manual screenshots** — real, but someone has to retake 11 images on every UI change.
3. **Capture in CI** — a tool starts the real app on a Windows runner with demo data and captures each window.
   Needs demo data, a database for the shared-database features (presence, offline) and keyboard automation.
4. **A demo mode inside the app** with fake sessions — no database needed, but ships fake-data code paths.

## Decision

Option 3, without changes to the app's behaviour:

* `tools/Harbor.Screenshots` (not shipped) references the app and runs next to `RemoteDesktopHarbor.exe` in
  **portable mode**, so it never touches real user data.
* It creates `HarborDemo` on **SQL Server LocalDB** (on the runner; installed if missing), writes settings for that
  shared database (*Not protected* key, *No sign-in* as "Alex Kim", light theme) and saves the fictional contoso.local
  catalog through the app's own `AppHost`/workspace. Two colleagues' presence records are real rows in
  `ActiveSessions`, so the in-use markers, the sessions table and the takeover question are the real thing.
* It starts the app, sizes the window to the runner's screen (1024 × 768 by default), drives it with `SendInput`
  (quick connect, F2, Ctrl+,, Ctrl+Tab, Esc) and copies each window's frame from the screen to a PNG. For the
  offline shot it takes the database offline (`ALTER DATABASE … SET OFFLINE`; stopping LocalDB would not work, it
  restarts on the next connection).
* `.github/workflows/screenshots.yml` runs it on pull requests that touch the app (artifact *screenshots*) and, run by
  hand with **commit**, commits the PNGs to the branch.
* The HTML mockups stay for shots the tool cannot take yet (`passphrase.png`) and for previewing designs;
  `render.cjs` skips captured shots unless `--all` is given.

## Consequences

* Docs show the real UI; updating them is one workflow run.
* The images are at 100 % scaling on a light theme, without the drop shadow of the mockups.
* Keyboard automation depends on shortcuts (Ctrl+K, F2, Ctrl+, …); a changed shortcut must be changed in the tool too.
* `Ctrl+,` (Settings) was added to the app, partly for this tool.
