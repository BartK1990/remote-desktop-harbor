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
| FR-01 | The app stores entries with: name, folder, host, port, domain, user name, password, notes, and (for RDP entries) additional `.rdp` settings. |
| FR-02 | Users can create, edit, delete and search entries (by name, host, user, folder, notes). Entries are grouped by folder. |
| FR-03 | Each entry is one of two kinds: **RDP connection** — a full Remote Desktop profile (host + `.rdp` settings) with optional credentials; **Credentials only** — just user name/password and an optional host; launching uses default RDP settings. |
| FR-04 | Passwords are encrypted by the app before they are written to any database and are decrypted only at the moment of use (launch, copy). |
| FR-05 | Existing `.rdp` files can be imported. Host, port, domain and user become fields; other settings are kept; any saved password in the file is discarded. |
| FR-06 | Users can copy the user name and the password to the clipboard. A copied password is cleared from the clipboard after 30 s if unchanged. |

### 3.2 Launching Remote Desktop

| ID | Requirement |
|---|---|
| FR-10 | Activating an entry (button, double-click, Enter) opens the standard Windows Remote Desktop client (`mstsc.exe`) as its own window — not embedded in the app — already connected with the stored credentials. |
| FR-11 | The password is handed to the client through Windows Credential Manager (`TERMSRV/<host>`), never via command line or `.rdp` file. By default the credential is removed shortly after launch and never survives logoff; a setting can keep it. |
| FR-12 | Temporary `.rdp` files are deleted after launch; leftovers from crashes are removed at next start. |

### 3.3 Storage

| ID | Requirement |
|---|---|
| FR-20 | **Local mode:** all data lives in a SQLite file in the user's profile (or next to the exe in portable mode). |
| FR-21 | **Remote mode:** all entries *including encrypted passwords* live in a shared SQL database (SQL Server / Azure SQL in v1). The design must allow adding other SQL engines without changing business logic. |
| FR-22 | In remote mode every client keeps a local SQLite copy of the shared data (still encrypted). When the shared database is unreachable, the app starts from and works with this copy: browse, search, copy, launch. Edits are disabled while offline and the UI says so. |
| FR-23 | The local copy is refreshed incrementally at start, periodically (default 60 s), on demand (F5), and after each own change. Deletions propagate. |
| FR-24 | Concurrent edits are detected (optimistic concurrency); a stale edit is rejected with a message, never silently overwrites. |
| FR-25 | The remote schema can be created/upgraded automatically by the app (optional; needs DDL rights) and is versioned. |

### 3.4 Session presence ("who is connected")

| ID | Requirement |
|---|---|
| FR-30 | When a user launches an entry and the shared database is reachable, the app writes a presence record: entry, Windows/Entra user name, display name, e-mail, machine name, start time. |
| FR-31 | Each record has an expiry (default TTL 120 s). While the RDP client runs, the app refreshes it (default every 45 s). All times use the database clock. |
| FR-32 | Clients list only non-expired records. A record whose app crashed, lost network or was killed disappears from everyone's view after at most one TTL. |
| FR-33 | Entries with active sessions show an "In use by …" badge and details (name, e-mail, machine, since). Connecting to an in-use entry asks for confirmation. |
| FR-34 | When the RDP client window closes, the record is deleted. Closing the app ends its records (after confirmation if sessions are still open). |
| FR-35 | If the database is unreachable at launch, the session still starts; presence is published as soon as the database is back. |
| FR-36 | Expired records older than one day are purged opportunistically. |

### 3.5 Identity

| ID | Requirement |
|---|---|
| FR-40 | Default sign-in uses the current Windows account without a prompt (domain or local). E-mail and display name come from Active Directory when reachable, otherwise from the UPN. |
| FR-41 | Optional Microsoft Entra ID sign-in via the Windows account broker (WAM): silent SSO on Entra/hybrid-joined devices, interactive otherwise. |
| FR-42 | The signed-in identity is used for presence (FR-30) and audit (`UpdatedBy` on entries). |
| FR-43 | Access to the shared SQL database uses the connection string's authentication: Windows integrated or Entra ID (`Authentication=Active Directory …`). |

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
| NFR-13 | A database administrator of the shared database without vault access sees only ciphertext. |
| NFR-20 | SQL engine specifics are isolated in one provider class per engine. |
| NFR-30 | Business logic, storage and crypto are covered by automated tests that run in CI on any OS. |

## 5. Data model

All timestamps are Unix milliseconds (UTC) from the database clock. Ids are GUID strings.

| Table | Columns | Notes |
|---|---|---|
| `Connections` | Id, Name, Folder, Kind, Host, Port, Domain, UserName, RdpSettings, Notes, PasswordCipher, Version, CreatedUtcMs, UpdatedUtcMs, UpdatedBy, IsDeleted | `Version` = optimistic concurrency token; deletes are tombstones so caches learn about them. |
| `ActiveSessions` | Id, ConnectionId, UserName, DisplayName, Email, MachineName, StartedUtcMs, LastHeartbeatUtcMs, ExpiresUtcMs | Presence (FR-30..36). Only used in the shared DB. |
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
* **Shared binding** ([ADR-0006](../adr/0006-shared-vault-key-protection.md)): in the shared DB the DEK is wrapped with
  DPAPI-NG to an AD security group (recommended) or with a passphrase-derived key (PBKDF2-SHA256, 600k iterations).
  After the first successful unwrap, each client caches the DEK locally (DPAPI user + machine) so offline mode works.
* **Threats addressed:** stolen laptop disk/DB copy (DPAPI binding), DB admin reading passwords (ciphertext only),
  row tampering (GCM + AAD), password leakage via process list/.rdp files (Credential Manager hand-off).
* **Out of scope / accepted:** a user who can unwrap the vault can read every password in it (that is the purpose
  of a shared vault); malware running as the same Windows user; key rotation (future work).

## 7. Settings

Stored as JSON in the data folder (`%LOCALAPPDATA%\RemoteDesktopHarbor\settings.json`, or `.\data` in portable mode).
The remote connection string is DPAPI-protected. See `HarborSettings` for the full list.

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
| FR-01..06 | `Core/Model/ConnectionEntry`, `App/ViewModels/*` | `SqlHarborStoreTests` |
| FR-03, FR-05, FR-10, FR-12 | `Core/Rdp/RdpFile`, `Windows/Rdp/MstscLauncher` | `RdpFileTests` |
| FR-11 | `Windows/Rdp/WindowsCredentialStore` | manual (Windows) |
| FR-20..25 | `Core/Services/HarborWorkspace`, `Data/SqlHarborStore`, `Data/Providers/*` | `WorkspaceTests`, `SqlHarborStoreTests` |
| FR-30..36 | `Core/Services/SessionPresenceService`, `Data/SqlHarborStore` | `PresenceTests` |
| FR-40..43 | `Windows/Identity/*`, `App/Infrastructure/AppHost` | manual (Windows) |
| NFR-10..13 | `Core/Security/*`, `Windows/Security/*` | `SecretCipherTests`, `WorkspaceTests` |
| NFR-01..05 | `App/*`, publish profile, workflows | CI publish |
