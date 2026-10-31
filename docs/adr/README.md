# Architecture Decision Records

Each ADR captures one significant decision: its context, the options considered, the choice and its consequences.
ADRs are immutable once accepted; to change a decision, write a new ADR that supersedes the old one.
Process: see [spec/README.md](../spec/README.md).

| ADR | Title | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Spec-driven development with ADRs | Accepted |
| [0002](0002-ui-framework-wpf-net10.md) | UI framework: WPF on .NET 10 | Accepted |
| [0003](0003-portable-self-contained-distribution.md) | Distribution: portable self-contained win-x64 exe, no install | Accepted |
| [0004](0004-data-access-ado-dapper-providers.md) | Data access: ADO.NET + Dapper with pluggable SQL providers | Accepted |
| [0005](0005-secret-encryption-envelope.md) | Secret encryption: AES-GCM envelope, DEK bound to user + machine with DPAPI | Accepted |
| [0006](0006-shared-vault-key-protection.md) | Shared vault key: DPAPI-NG to an AD group, passphrase fallback | Accepted, amended by 0016 |
| [0007](0007-remote-store-with-offline-cache.md) | Remote store as source of truth with local read-only offline cache | Accepted |
| [0008](0008-launch-mstsc-with-credential-manager.md) | Launch mstsc.exe, hand credentials over via Credential Manager | Accepted |
| [0009](0009-identity-windows-and-entra-id.md) | Identity: Windows account by default, Entra ID via WAM broker | Accepted |
| [0010](0010-session-presence-heartbeat-ttl.md) | Session presence: heartbeat records with server-clock TTL | Accepted |
| [0011](0011-ci-release-pipeline.md) | CI and release on GitHub Actions, win-x64 | Accepted |
| [0012](0012-microsoft-store-readiness.md) | Microsoft Store readiness via MSIX packaging later | Accepted |
| [0013](0013-documentation-site-starlight.md) | Documentation site: Astro Starlight on GitHub Pages | Accepted |
| [0014](0014-database-connection-form.md) | Shared database configured by form fields via the provider; optional database creation | Accepted |
| [0015](0015-anonymous-named-mode.md) | Optional no-sign-in mode with a user-entered name | Accepted |
| [0016](0016-sign-in-vault-key-protection.md) | Shared vault key protected by verified sign-in (no passphrase); one-time conversion | Accepted |
| [0017](0017-compact-folder-tree.md) | Compact folder tree for the entry list | Accepted |
| [0018](0018-database-sign-in-opens-vault.md) | Database sign-in alone opens a `sign-in` vault (no app sign-in required) | Accepted |
| [0019](0019-multiple-accounts-per-entry.md) | Several user accounts per entry in a child table; the entry's own account stays the default | Accepted |
| [0020](0020-wpf-ui-library.md) | Fluent look with WPF UI (lepoco) instead of the built-in WPF Fluent theme | Superseded by 0021 |
| [0021](0021-keep-built-in-fluent-theme.md) | Keep the built-in WPF Fluent theme (WPF UI reverted) | Accepted |
| [0022](0022-rdp-settings-form.md) | RDP settings edited as a form over the stored `.rdp` text | Accepted |
| [0023](0023-tabbed-settings-dialog.md) | Settings dialog on tabs with compact, paired fields | Accepted (amended by 0027) |
| [0024](0024-tabbed-entry-editor.md) | Entry editor on tabs with compact, paired fields | Accepted (amended by 0027) |
| [0025](0025-theme-tokens.md) | Harbor colour and type tokens on top of the built-in Fluent theme | Accepted |
| [0026](0026-main-window-layout.md) | Main window laid out for keyboard-driven engineers | Accepted |
| [0027](0027-side-navigation-dialogs.md) | Side navigation in the editor and Settings; theme chosen in Settings | Accepted |
| [0028](0028-quick-connect-tags-pins.md) | Quick connect palette, environment tags from folder names, personal pins and recents | Accepted |
| [0029](0029-app-title-bar.md) | The app draws its own narrow title bar; production entries ask before connecting | Accepted |
