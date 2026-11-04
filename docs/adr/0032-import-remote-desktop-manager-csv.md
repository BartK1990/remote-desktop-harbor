# ADR-0032: Import from a Remote Desktop Manager CSV export

* Status: Accepted
* Date: 2026-11-04
* Requirements: FR-54, FR-03, FR-04, FR-09, NFR-12

## Context

Many teams moving to Harbor keep their catalog in Devolutions Remote Desktop Manager (RDM). Retyping hundreds of
entries is not realistic. RDM's "Export entries (CSV)" writes one row per entry with the columns `ConnectionType`,
`ConnectionSubType`, `Name`, `Group`, `Description`, `Keywords`, `Host`, `Port`, `CredentialUserName`,
`CredentialDomain`, `CredentialPassword`, `OpenInConsole`, `URL`, `DataEntry.Web*` and `WebUrl` (an `rdm://` link).
Passwords are in plain text; descriptions contain line breaks inside quoted fields. An export mixes RDP sessions with
folders, credentials, VNC, TeamViewer, web, Citrix and VPN entries.

## Options considered

1. **Import other connection types as credentials-only** — keeps the passwords, but Harbor launches RDP for those,
   so a VNC or web entry would connect with the wrong protocol and port.
2. **Import RDP sessions and "Username and password" entries; skip the rest** — loses the credentials of every VNC,
   TeamViewer, web and VPN entry, which users want to keep in one place.
3. **Import the rest as a new kind, Other, that is never launched** ([ADR-0033](0033-other-entry-kind.md)) — keeps every
   credential without pretending Harbor can open it.
4. **Import through the RDM `.rdm` XML export** — richer, but undocumented and version-dependent; CSV is what users
   asked for.

## Decision

Option 3. `Harbor.Core/Import/RdmCsvImport` (OS-neutral, unit-tested) reads the CSV with an RFC 4180 reader (quoted
fields with delimiters, doubled quotes and line breaks; BOM; `,` or `;` chosen from the header), finds columns by
name and maps rows as in FR-54. The folder comes from `Group`, so RDM folder rows are not needed and are skipped.
`OpenInConsole=Yes` becomes `administrative session:i:1`. Other entries start their notes with `Type: VNC (TightVNC)`
and the `URL`, so users know how to open them. Web credentials (`DataEntry.Web*`) become the entry's credentials when
it has none, otherwise an additional account. `WebUrl` (an `rdm://` link back into RDM) and `Expiration` are ignored.

The main window shows one confirmation with what will be added, left out and skipped, then saves each entry through
`HarborWorkspace.SaveAsync` with `SecretUpdate.Set`, so passwords are encrypted exactly like typed ones and never
logged (`ImportedEntry.ToString` omits the password). Entries already in the catalog — same kind, name, folder, host,
port and qualified user — are left out, so importing an export twice adds nothing; repeats inside one file are kept.

## Consequences

* Migrating from RDM is one click plus one confirmation; a mistake is undone by deleting the imported folder entries.
* Nothing but folder rows is dropped; Other entries hold the credentials and say what they belong to.
* In remote mode each entry is one round trip; an export of a few thousand entries takes seconds. A failure stops the
  import and says how many entries were saved; running it again skips those.
* The source CSV keeps plain-text passwords; the confirmation tells the user to delete it. Harbor does not touch it.
