# Remote Desktop Harbor — Product Specification

| | |
|---|---|
| Status | Accepted (v0.1) |
| Platform | Windows 10 1809+ / Windows 11, x64 |
| Decisions | [ADR index](../adr/README.md) |

## 1. Purpose

A fast Windows desktop app that keeps a catalog of Remote Desktop connections with their credentials,
opens them in the regular Windows Remote Desktop client with one click, and — when a team shares a database —
shows who is currently connected to which machine.

## 2. Users and context

* IT admins / engineers who connect to many Windows servers.
* Standard (non-admin) Windows accounts, domain-joined, Entra-joined or local.
* Either working alone (local vault) or as a team sharing one SQL database.

## 3. Functional requirements

### 3.1 Catalog

| ID | Requirement |
|---|---|
| FR-01 | The app stores entries with: name, folder, host, port, domain, user name, password, notes, additional accounts (FR-09), and (for RDP entries) additional `.rdp` settings. |
| FR-02 | Users can create, edit, delete and search entries (by name, host, user, folder, notes). Entries are shown in a compact folder tree: one low row per folder or entry with an icon (folder, RDP connection, credentials only) and its name; entry rows also show the host on the right in a monospace font, with the port when it is not 3389 (or the in-use marker, FR-33). Folders can be nested (`Production/Web`; `/` or `\` separates levels) and each can be expanded and collapsed; collapsed folders are remembered across restarts. The search filters the tree as you type and expands every folder with matches. See [ADR-0017](../adr/0017-compact-folder-tree.md), [ADR-0025](../adr/0025-theme-tokens.md). |
| FR-03 | Each entry is one of two kinds: **RDP connection** — a full Remote Desktop profile (host + `.rdp` settings) with optional credentials; **Credentials only** — just user name/password and an optional host; launching uses default RDP settings. |
| FR-04 | Passwords are encrypted by the app before they are written to any database and are decrypted only at the moment of use (launch, copy). |
| FR-05 | Existing `.rdp` files can be imported. Host, port, domain and user become fields; other settings are kept; any saved password in the file is discarded. |
| FR-06 | Users can copy the user name, the password and the host (`host`, or `host:port` for a non-default port) to the clipboard. A copied password is cleared from the clipboard after 30 s if unchanged. |
| FR-07 | The app shows its release version (e.g. `0.1.0`) in the main window title and in Settings → About, together with the build commit when known. The version comes from the release build (`-p:Version`), so the app and its GitHub release always match. |
| FR-08 | The user can choose the colour theme — **System** (follow the Windows light/dark setting, default), **Light** or **Dark** — in **Settings → Appearance**. The theme previews as soon as it is picked; **Save** keeps it without a restart (and without the restart prompt when nothing else changed), **Cancel** goes back to the saved theme. The choice is remembered across restarts. The colours the app adds to the theme (in use, online, offline, errors) have a light and a dark variant that follow the theme, including a Windows light/dark switch while **System** is chosen; views use no fixed colours. Host, port and user names are shown in a monospace font. See [ADR-0025](../adr/0025-theme-tokens.md), [ADR-0027](../adr/0027-side-navigation-dialogs.md). |
| FR-09 | An entry can have **several accounts**, for hosts that allow several sessions at once (e.g. Windows Server): the entry's own domain/user name/password is its default account, followed by any number of additional accounts (user name, optional domain — empty uses the entry's — and password). In the tree an entry with additional accounts can be expanded to one row per account (default first). Connecting and copying the user name or password use the selected account row, or the default account when the entry row is selected. Each account's password is encrypted on its own and bound to that account (FR-04, NFR-11). Search also matches account user names. See [ADR-0019](../adr/0019-multiple-accounts-per-entry.md). |
| FR-47 | The main window is built for keyboard use. **Title bar** (FR-52): the Quick connect field (FR-48) and the signed-in user. **Toolbar:** search box with a placeholder and its shortcut, icon buttons for New, Import .rdp… and Refresh, and a Settings button (the theme is chosen in Settings, FR-08). **Details pane** of the selected entry: folder and kind, name, host; **Connect as &lt;account&gt;** naming the account it uses, with a menu of the entry's accounts (FR-09); Copy password, Copy user, Edit and a More menu (Copy host, Delete); an active-sessions table (user, account, machine, since) that says whether connecting with the selected account takes a session over (FR-33); cards for the connection (host, port, user, password, each with a copy button), a summary of the RDP settings (RDP connections) and the notes; the last change and the entry version. **Status bar:** last status, number of entries in use, storage state with an online/offline dot. **Shortcuts**, shown next to their actions or in their tooltips: Ctrl+K quick connect (FR-48), Ctrl+F search, Ctrl+D pin (FR-50), Ctrl+N new, F2 edit, Del (in the tree) delete, Ctrl+Shift+C / U / H copy password / user / host, F5 refresh. See [ADR-0026](../adr/0026-main-window-layout.md). |
| FR-48 | **Quick connect** (Ctrl+K, or the toolbar button) opens a palette over the main window: a query box and up to 12 results, one per entry and one per additional account (FR-09), each with name, host, user, folder, environment tag (FR-49), pin star and in-use marker (FR-33). Every word of the query must match the name, host, user or folder; name matches rank first (exact, prefix, word start, anywhere), then host, user and folder; pinned and recent entries (FR-50) come first among equals, and an empty query lists them. Up/Down pick a result, Enter or a double-click selects it in the tree and connects with its account, Shift+Enter only selects it, Ctrl+Shift+C / U / H copy its password / user / host and F2 edits it (each selects it and closes the palette), Esc or a click outside closes the palette. See [ADR-0028](../adr/0028-quick-connect-tags-pins.md). |
| FR-49 | **Environment tags** are derived from folder names: a folder level whose name contains a whole word *prod/production/prd/live*, *staging/stage/stg/preprod/uat*, *test/testing/tst/qa*, *dev/development* or *lab/sandbox* is tagged **PROD**, **STG**, **TEST**, **DEV** or **LAB**. The folder row shows its own tag; an entry takes the tag of the outermost tagged folder above it and shows it in the details pane and in quick connect. Tags have their own light and dark colours and always show their text. No configuration or schema change. See [ADR-0028](../adr/0028-quick-connect-tags-pins.md). |
| FR-50 | Users can **pin** entries (star in the details pane, context menu, Ctrl+D); pinned entries show a star in the tree. The app remembers the last 10 entries the user connected to. Filters above the tree show **All**, **In use**, **Pinned** or **Recent** entries, combined with the search. Pins and recent entries are personal: kept in the user's settings (`PinnedEntryIds`, `RecentEntryIds`), never in a shared database. See [ADR-0028](../adr/0028-quick-connect-tags-pins.md). |
| FR-51 | Connecting to an entry tagged **PROD** (FR-49) asks for confirmation first, naming the entry, its folder and the account. When the connection would also take over a session (FR-33) the takeover question mentions production instead, so at most one question is asked. Settings → Remote Desktop can turn the production question off (`ConfirmProductionConnect`, default on). See [ADR-0029](../adr/0029-app-title-bar.md). |
| FR-52 | Every window draws the app's own **title bar**, 32 px high, in the app's theme: icon, title (dimmed while the window is inactive), optional content and caption buttons drawn like the system's (Minimize, Maximize/Restore, Close; dialogs show Close only; Close turns red on hover). The main window puts the Quick connect field (FR-48) in the middle and the signed-in user on the right. Dragging, double-click to maximize, Aero snap, resizing from every edge, the window shadow and the system menu (Alt+Space, right-click) keep working; a maximized window keeps its content on screen. See [ADR-0029](../adr/0029-app-title-bar.md). |

### 3.2 Launching Remote Desktop

| ID | Requirement |
|---|---|
| FR-10 | Activating an entry (button, double-click, Enter) opens the standard Windows Remote Desktop client (`mstsc.exe`) as its own window — not embedded in the app — already connected with the stored credentials. |
| FR-11 | The password is handed to the client through Windows Credential Manager (`TERMSRV/<host>`), never via command line or `.rdp` file. By default the credential is removed shortly after launch and never survives logoff; a setting can keep it. Accounts of one host share that credential: a launch's cleanup never removes a credential written by a later launch, and launching an account without a stored password first removes a credential the app wrote moments before for another account (FR-09). |
| FR-12 | Temporary `.rdp` files are deleted after launch; leftovers from crashes are removed at next start. |
| FR-13 | The entry editor shows the common `.rdp` settings of an RDP connection as form fields instead of raw text: display mode (full screen / window), resolution (match local screen, common presets or a custom width × height of 200–8192 px), colour depth, all monitors, dynamic resolution, smart sizing; remote audio, microphone, Windows key combinations, clipboard, printers, smart cards, all local drives; server authentication, automatic reconnect, administrative session. Only values that differ from the launch defaults are stored. Any other line — unknown keys or values the form cannot show — is kept verbatim in an "Other .rdp settings" box. The stored format stays `.rdp` text (no schema change). See [ADR-0022](../adr/0022-rdp-settings-form.md). |

### 3.3 Storage

| ID | Requirement |
|---|---|
| FR-20 | **Local mode:** all data lives in a SQLite file in the user's profile (or next to the exe in portable mode). |
| FR-21 | **Remote mode:** all entries *including encrypted passwords* live in a shared SQL database (SQL Server / Azure SQL in v1). The design must allow adding other SQL engines without changing business logic. |
| FR-22 | In remote mode every client keeps a local SQLite copy of the shared data (still encrypted). When the shared database is unreachable, the app starts from and works with this copy: browse, search, copy, launch. Edits are disabled while offline and the UI says so. |
| FR-23 | The local copy is refreshed incrementally at start, periodically (default 60 s), on demand (F5), and after each own change. Deletions propagate. |
| FR-24 | Concurrent edits are detected (optimistic concurrency); a stale edit is rejected with a message, never silently overwrites. |
| FR-25 | The remote schema can be created/upgraded automatically by the app (optional; needs DDL rights) and is versioned. |
| FR-26 | The Settings dialog always opens showing the saved configuration (storage mode, key protection, sign-in). Choosing an option in one section never changes another section. |
| FR-28 | The shared vault key can be protected by **database sign-in** (default, scheme `sign-in`): any user who can sign in to the shared database — Windows integrated, SQL login (user + password) or Entra ID — opens the vault without a passphrase, whatever the app sign-in (Windows, Entra ID or none, FR-44). An existing vault can be converted to another protection once, on request, keeping its key ([ADR-0016](../adr/0016-sign-in-vault-key-protection.md), [ADR-0018](../adr/0018-database-sign-in-opens-vault.md)). |
| FR-27 | The shared database is configured with simple fields — server (name or IP), database name, database sign-in (Windows, SQL login with user + password, Entra ID), encryption options — with an option to create the database if it does not exist ([ADR-0014](../adr/0014-database-connection-form.md)). A raw connection string can still be edited; strings the fields cannot describe are kept as text. |

### 3.4 Session presence ("who is connected")

| ID | Requirement |
|---|---|
| FR-30 | When a user launches an entry and the shared database is reachable, the app writes a presence record: entry, the account connected as (FR-09), Windows/Entra user name, display name, e-mail, machine name, start time. |
| FR-31 | Each record has an expiry (default TTL 120 s). While the RDP client runs, the app refreshes it (default every 45 s). All times use the database clock. |
| FR-32 | Clients list only non-expired records. A record whose app crashed, lost network or was killed disappears from everyone's view after at most one TTL. |
| FR-33 | Entries with active sessions show an in-use marker in the tree — a dot and the user's name (or "N users"), its accessible name "In use by …" — and details (name, e-mail, machine, account, since); account rows (FR-09) show the marker for sessions using that account. Connecting asks for confirmation when a session uses the same account — or a record names no account (older clients) or the connection has no user name — because that would take the session over; sessions with other accounts do not block, as the host can run them side by side. |
| FR-34 | When the RDP client window closes, the record is deleted. Closing the app ends its records (after confirmation if sessions are still open). |
| FR-35 | If the database is unreachable at launch, the session still starts; presence is published as soon as the database is back. |
| FR-36 | Expired records older than one day are purged opportunistically. |

### 3.5 Identity

| ID | Requirement |
|---|---|
| FR-40 | Default sign-in uses the current Windows account without a prompt (domain or local). E-mail and display name come from Active Directory when reachable, otherwise from the UPN. |
| FR-41 | Optional Microsoft Entra ID sign-in via the Windows account broker (WAM): silent SSO on Entra/hybrid-joined devices, interactive otherwise. |
| FR-44 | Optional **no sign-in** mode: the user enters a name (required), used for presence and audit and shown as "(not signed in)". It is unverified, so Windows or Entra ID sign-in stays the default and recommended ([ADR-0015](../adr/0015-anonymous-named-mode.md)). |
| FR-45 | The Settings dialog is split into tabs, listed in a side navigation — **Storage** (local vault or shared database, chosen as two cards, and the connection), **Shared vault** (vault key protection, schema, sync and presence timings; disabled with a hint while local storage is chosen), **Sign-in**, **Remote Desktop**, **Appearance** (theme, FR-08) and **About** (version, data folder with an Open button) — so no tab needs a long scroll. Below the connection fields a read-only preview shows the connection string they produce, with the password masked; the Test connection result is shown green (OK) or red (failed). <kbd>Ctrl+S</kbd> saves, <kbd>Esc</kbd> cancels; Save asks to restart only when something besides the theme changed. Related short fields share a row (server + database name, user name + password, encrypt + trust server certificate, client ID + tenant), short values get narrow boxes, and every tab keeps a gutter so its scroll bar never covers an input. Validation errors show below the tabs, whatever tab is open. See [ADR-0023](../adr/0023-tabbed-settings-dialog.md), [ADR-0027](../adr/0027-side-navigation-dialogs.md). |
| FR-46 | The entry editor is split into tabs, listed in a side navigation — **General** (type, name + folder, host + port, domain + user name, password, notes), **Accounts** (additional accounts, FR-09), and for RDP connections only **Display**, **Local resources** and **Connection** (the RDP settings form and "Other .rdp settings", FR-13) — so no tab needs a long scroll. Related short fields share a row, each additional account fits on one row (domain, user name, password, Remove), and every tab keeps a gutter so its scroll bar never covers an input. Validation errors show below the tabs, whatever tab is open; a failed save also opens the tab with the problem and puts the cursor in the field. The type is a two-way switch; host, port, domain, user name and folder use the monospace font. A stored password shows as "Stored, encrypted" with **Replace…** and **Remove** (and **Keep stored password** to undo), instead of a "change" checkbox; accounts likewise. On save, `host:port` typed in Host is split into Host and Port (when Port is still 3389 or the same), and `DOMAIN\user` typed in User name into Domain and User name (when Domain is empty). <kbd>Ctrl+S</kbd> saves, <kbd>Esc</kbd> cancels. See [ADR-0024](../adr/0024-tabbed-entry-editor.md), [ADR-0027](../adr/0027-side-navigation-dialogs.md). |
| FR-42 | The signed-in identity is used for presence (FR-30) and audit (`UpdatedBy` on entries). |
| FR-43 | Access to the shared SQL database uses the connection string's authentication: Windows integrated, SQL Server login, or Entra ID (`Authentication=Active Directory …`). It is independent of the app sign-in (FR-40/41): every sign-in works with both storage modes. |

## 4. Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-01 | **Performance first.** Main window visible in < 1 s on typical hardware; catalog shown from the local SQLite file before any network call; sign-in, sync and presence never block the UI thread. |
| NFR-02 | Runs **without administrator rights** and **without installation**: a single self-contained `RemoteDesktopHarbor.exe`; per-user data; manifest `asInvoker`. |
| NFR-03 | Smooth with 10 000 entries (UI virtualization, in-memory filtering). |
| NFR-04 | Default CI and release target: **Windows x64**. |
| NFR-05 | Must stay publishable to the **Microsoft Store** (MSIX) later without architectural changes. |
| NFR-10 | Secrets are encrypted with AES-256-GCM. Local keys are bound to the **Windows user and the machine** (DPAPI); a copied database cannot be decrypted elsewhere. |
| NFR-11 | Ciphertexts are bound to their entry (associated data); swapped or tampered ciphertexts fail to decrypt. |
| NFR-12 | No plaintext secret on disk, in `.rdp` files, on command lines or in logs. |
| NFR-13 | With AD-group or passphrase protection, a database administrator of the shared database without vault access sees only ciphertext. With sign-in protection (FR-28) database access is vault access — an accepted trade-off chosen in Settings. |
| NFR-20 | SQL engine specifics are isolated in one provider class per engine. |
| NFR-30 | Business logic, storage and crypto are covered by automated tests that run in CI on any OS. View-model tests that need WPF or DPAPI (`Harbor.App.Tests`) run on Windows only (CI). |

## 5. Data model

All timestamps are Unix milliseconds (UTC) from the database clock. Ids are GUID strings.

| Table | Columns | Notes |
|---|---|---|
| `Connections` | Id, Name, Folder, Kind, Host, Port, Domain, UserName, RdpSettings, Notes, PasswordCipher, Version, CreatedUtcMs, UpdatedUtcMs, UpdatedBy, IsDeleted | `Version` = optimistic concurrency token; deletes are tombstones so caches learn about them. |
| `ConnectionAccounts` | Id, ConnectionId, Position, Domain, UserName, PasswordCipher | Additional accounts of an entry (FR-09), written with the entry in one transaction; the entry's `Version` covers them. Removed when the entry is deleted. |
| `ActiveSessions` | Id, ConnectionId, UserName, DisplayName, Email, MachineName, StartedUtcMs, LastHeartbeatUtcMs, ExpiresUtcMs, AccountName | Presence (FR-30..36). Only used in the shared DB. `AccountName` is the remote account (FR-09); null from older clients. |
| `VaultKeys` | Slot, KeyId, Scheme, ProtectedKey, CreatedUtcMs | Wrapped data-encryption key. `Scheme` names the protector. |
| `Meta` | Key, Value | e.g. sync watermark in the cache. |
| `SchemaVersion` | Version | Applied migrations. |

## 6. Security model

```
                      ┌──────────────── shared SQL DB ────────────────┐
 password ─AES-GCM──► │ PasswordCipher (DEK)   VaultKeys: DEK wrapped │
                      │                        with DPAPI-NG (AD group)│
                      │                        or passphrase           │
                      └───────────────────────────┬───────────────────┘
                                                  │ sync (ciphertext only)
                      ┌──────────── local SQLite (cache / vault) ─────┐
                      │ PasswordCipher (same DEK)  VaultKeys: DEK      │
                      │                            wrapped with DPAPI  │
                      │                            user + machine      │
                      └───────────────────────────────────────────────┘
```

* **Envelope encryption** ([ADR-0005](../adr/0005-secret-encryption-envelope.md)): one random 256-bit data-encryption
  key (DEK) per vault encrypts all passwords (AES-256-GCM, entry id as associated data).
* **Local binding:** the DEK in any local SQLite file is wrapped with DPAPI *CurrentUser* and then *LocalMachine*:
  only this Windows user on this computer can unwrap it.
* **Shared binding** ([ADR-0006](../adr/0006-shared-vault-key-protection.md), [ADR-0016](../adr/0016-sign-in-vault-key-protection.md)):
  by default the DEK is stored **unencrypted** in the shared DB (scheme `sign-in`, shown as *Not protected*; no
  passphrase; anyone who can read the database or a backup can open it, including a DBA, ADR-0018). Alternatively in the shared DB the DEK is wrapped with
  DPAPI-NG to an AD security group (recommended) or with a passphrase-derived key (PBKDF2-SHA256, 600k iterations).
  After the first successful unwrap, each client caches the DEK locally (DPAPI user + machine) so offline mode works.
* **Threats addressed:** stolen laptop disk/DB copy (DPAPI binding), DB admin reading passwords (ciphertext only — when the shared DEK is wrapped with DPAPI-NG or a passphrase,
  not with the default *Not protected*),
  row tampering (GCM + AAD), password leakage via process list/.rdp files (Credential Manager hand-off).
* **Out of scope / accepted:** a user who can unwrap the vault can read every password in it (that is the purpose
  of a shared vault); malware running as the same Windows user; key rotation (future work).

## 7. Settings

Stored as JSON in the data folder (`%LOCALAPPDATA%\RemoteDesktopHarbor\settings.json`, or `.\data` in portable mode).
The remote connection string — including a SQL login password, if used — is DPAPI-protected. See `HarborSettings`
for the full list.

## 8. Open questions

| # | Question | Current assumption |
|---|---|---|
| Q1 | "Store RDP or credentials only" — per entry or a global mode? | Per entry (FR-03). A global default can be added without schema change. |
| Q2 | Should offline edits be queued and replayed? | No in v1 (read-only offline, FR-22) to avoid merge conflicts on secrets. |
| Q3 | Should presence also be detected for sessions started outside the app? | No; only sessions launched from the app. |
| Q4 | Per-folder access control in the shared DB? | Not in v1; everyone with vault access sees all entries. |

## 9. Traceability

| Requirement | Implementation | Tests |
|---|---|---|
| FR-01..06 | `Core/Model/ConnectionEntry`, `App/ViewModels/*` | `SqlHarborStoreTests`, `AppearanceTests` (host text) |
| FR-09 | `Core/Model/ConnectionAccount`, `Core/Model/ConnectionEntry`, `Core/Services/HarborWorkspace`, `Data/SqlHarborStore`, `Data/Providers/*` (migration 2), `Windows/Rdp/MstscLauncher`, `App/ViewModels/EntryViewModel`, `EntryEditorViewModel`, `MainViewModel`, `MainWindow`, `EntryEditorWindow` | `ConnectionAccountTests`, `AccountTests`, `AccountViewModelTests` (Windows) |
| FR-02 (tree) | `Core/Model/FolderTree`, `Core/Model/ConnectionEntry` (`Address`), `App/ViewModels/MainViewModel`, `TreeNodeViewModel`, `EntryViewModel`, `MainWindow` | `FolderTreeTests`, `AppearanceTests`; UI manual (Windows) |
| FR-07 | `App/Infrastructure/AppInfo`, `MainWindow`, `SettingsWindow` | manual (Windows) |
| FR-08 | `Core/HarborSettings` (`AppThemeExtensions`), `App/Infrastructure/ThemeService`, `App/Themes/*`, `Windows/Theming/WindowsAppMode`, `App/ViewModels/SettingsViewModel`, `MainViewModel`, `SettingsWindow`, `App/Views/EnumToBooleanConverter` | `AppearanceTests`, `ViewSourceTests`, `DialogViewModelTests` (Windows); manual (Windows) |
| FR-03, FR-05, FR-10, FR-12 | `Core/Rdp/RdpFile`, `Windows/Rdp/MstscLauncher` | `RdpFileTests` |
| FR-13 | `Core/Rdp/RdpOptions`, `App/ViewModels/RdpSettingsEditorViewModel`, `EntryEditorViewModel`, `EntryEditorWindow` | `RdpOptionsTests`, `RdpSettingsEditorViewModelTests` (Windows) |
| FR-11 | `Windows/Rdp/WindowsCredentialStore` | manual (Windows) |
| FR-20..25 | `Core/Services/HarborWorkspace`, `Data/SqlHarborStore`, `Data/Providers/*` | `WorkspaceTests`, `SqlHarborStoreTests` |
| FR-22 (shared key cached per machine) | `Core/Services/HarborWorkspace` | `WorkspaceTests` |
| FR-28 | `Core/Security/SignInKeyProtector`, `Core/Services/HarborWorkspace`, `App/ViewModels/SettingsViewModel` | `SignInKeyProtectorTests`, `WorkspaceTests`, `SettingsViewModelTests` (Windows) |
| FR-26 | `App/ViewModels/SettingsViewModel`, `App/Views/SettingsWindow` | `SettingsViewModelTests`, `ViewSourceTests` (Windows) |
| FR-27 | `Data/Providers/IServerDatabaseProvider`, `Data/Providers/SqlServerProvider`, `App/ViewModels/SettingsViewModel` | `SqlServerConnectionFormTests`, `SettingsViewModelTests` (Windows) |
| FR-47 | `Core/Rdp/RdpSummary`, `Core/Model/ActiveSession` (`TakenOverBy`), `App/Themes/Tokens`, `App/ViewModels/MainViewModel`, `EntryViewModel`, `MainWindow` | `DetailsPaneTests`, `ViewSourceTests` (Windows); manual (Windows) |
| FR-48 | `Core/Services/QuickSearch`, `App/ViewModels/MainViewModel`, `QuickConnectItem`, `MainWindow` | `QuickConnectTests`, `QuickConnectViewModelTests`, `ViewSourceTests` (Windows); manual (Windows) |
| FR-49 | `Core/Model/EnvironmentTags`, `App/ViewModels/EntryViewModel`, `TreeNodeViewModel`, `App/Themes/Colors.*`, `MainWindow` | `QuickConnectTests`, `QuickConnectViewModelTests` (Windows); manual (Windows) |
| FR-50 | `Core/HarborSettings`, `Core/Model/RecentList`, `App/ViewModels/MainViewModel`, `MainWindow` | `QuickConnectTests`, `QuickConnectViewModelTests` (Windows); manual (Windows) |
| FR-51 | `Core/HarborSettings`, `App/ViewModels/MainViewModel`, `SettingsViewModel`, `SettingsWindow` | manual (Windows) |
| FR-52 | `App/Views/TitleBar`, `App/Themes/Tokens` (caption buttons), `App/Themes/Colors.*`, every window | `ViewSourceTests` (Windows); manual (Windows) |
| FR-34 (closing the app) | `App/Views/MainWindow` | `ViewSourceTests` (Windows), manual |
| FR-30..36 | `Core/Services/SessionPresenceService`, `Data/SqlHarborStore` | `PresenceTests`, `AccountTests` (account in use) |
| FR-40..43 | `Windows/Identity/*`, `App/Infrastructure/AppHost` | manual (Windows) |
| FR-44 | `Core/Identity/AnonymousIdentityProvider`, `App/ViewModels/SettingsViewModel` | `AnonymousIdentityTests`, `SettingsViewModelTests` |
| FR-45 | `App/Views/SettingsWindow`, `App/ViewModels/SettingsViewModel`, `App/Themes/Tokens`, `Data/Providers/ConnectionStringMask` | `ConnectionStringMaskTests`, `ViewSourceTests`, `DialogViewModelTests` (Windows), manual (Windows) |
| FR-46 | `App/Views/EntryEditorWindow`, `App/ViewModels/EntryEditorViewModel`, `Core/Model/EntryInput`, `App/Themes/Tokens` | `EntryInputTests`, `ViewSourceTests`, `DialogViewModelTests` (Windows), manual (Windows) |
| NFR-10..13 | `Core/Security/*`, `Windows/Security/*` | `SecretCipherTests`, `WorkspaceTests` |
| NFR-01..05 | `App/*`, publish profile, workflows | CI publish |
