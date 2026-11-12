# ADR-0046: Backups are one passphrase-encrypted file, restored like an import

* Status: Accepted
* Date: 2026-11-12
* Requirements: FR-68, FR-04, FR-09, FR-29, FR-50, FR-54, FR-62, NFR-10, NFR-11, NFR-12

## Context

Users change computers and want to take their list of entries with them, or keep a copy. In local mode the catalog is
a SQLite file whose vault key is wrapped with DPAPI for the Windows user and machine (NFR-10): copying the file to a
new PC does not work by design — its passwords cannot be decrypted there. Pins (FR-50) and smart groups (FR-62) live in
the user's settings and are lost the same way. Entries kept on this PC only in shared mode (FR-29) have the same problem.

## Options considered

1. **Copy the vault file and re-wrap its key** — would need an export of the raw vault key; one leaked file would open
   the vault forever, and it does not cover shared mode or settings.
2. **Plain-text export (CSV/JSON) with passwords** — simple and readable, but leaves every password on disk in clear
   text (NFR-12), on USB sticks and cloud drives.
3. **Export without passwords** — safe, but the user has to retype every password on the new PC, which defeats the
   purpose.
4. **One file encrypted with a passphrase the user chooses** — the passphrase is independent of the PC, so any PC can
   restore it; the file is useless without it.

## Decision

Option 4. **Export backup…** writes a `.harborbackup` file: a small JSON envelope
(`format`, `version`, `kdf` = PBKDF2-SHA256, `iterations` = 600 000, 16-byte `salt`, `cipher` = AES-256-GCM, 12-byte
`nonce`, `tag`, `data`) whose `data` is the encrypted JSON of the content — entries with accounts and plaintext
passwords, their pin state, and the smart groups. The envelope fields are bound to the ciphertext as associated data,
so a changed header fails to decrypt like a wrong passphrase. The passphrase is typed twice and has at least 12
characters; it is never stored. The plaintext only exists in memory and its buffers are zeroed after use. The same
PBKDF2 parameters as the passphrase key protector (ADR-0006) are used. The vault key is not exported.

**Restore backup…** works like the RDM import (ADR-0032): one confirmation, entries already in the catalog (same kind,
name, folder, host, port, user) are left out, the rest are saved through `HarborWorkspace.SaveAsync` with new ids, so
each password is encrypted with this PC's key and bound to its new id (NFR-11). A pin in the backup pins the restored
entry, or its existing twin. Entries that were on the old PC only go to this PC's local vault when it has one; smart
groups are added unless one with the same name and folder exists. Recent entries and other settings (theme, sizes,
storage) are not part of a backup: they are tied to the PC or the shared database.

The code is OS-neutral (`Harbor.Core/Backup`); the app only picks files and asks for the passphrase.

## Consequences

* Moving to a new PC is export, copy one file, restore — in local mode and for entries on this PC only in shared mode.
* The backup is as strong as its passphrase; a forgotten passphrase cannot be recovered. The dialog says so.
* In shared mode an export also holds the shared entries' passwords, readable by whoever has the file and the
  passphrase — like copying them one by one, which a user with access can already do.
* Restoring into a shared database adds the entries for everyone; the confirmation says so.
* `version` lets a later format add fields; a newer backup is refused with a message to update the app.
