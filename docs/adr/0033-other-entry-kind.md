# ADR-0033: A third entry kind, Other, for credentials of non-RDP systems

* Status: Accepted
* Date: 2026-11-04
* Requirements: FR-03, FR-54

## Context

Entries were either RDP connections or credentials only, and both are launched with Remote Desktop. Catalogs
imported from Remote Desktop Manager (FR-54, [ADR-0032](0032-import-remote-desktop-manager-csv.md)) also hold
credentials for VNC, TeamViewer, web portals, Citrix, VPNs and iLO boards. Users want to keep those passwords in
Harbor too, but opening them with `mstsc.exe` would be wrong.

## Options considered

1. **Store them as credentials only** — no new concept, but Connect would start Remote Desktop to a VNC or web host.
2. **A per-entry "protocol" with launchers for VNC, web, …** — the most useful, but a large feature with a schema
   change and third-party clients.
3. **A third kind, Other (`Kind = 2`), never launched** — keeps the credentials safely, copy host/user/password works,
   no schema change.

## Decision

Option 3. `EntryKind.Other = 2` is stored in the existing integer `Kind` column. `ConnectionEntry.CanLaunch` is false
for it, so Connect, Enter and quick connect do nothing and `RdpFile.Build` refuses it. The editor's type switch gets a
third segment, **Other**; the RDP tabs are hidden as for credentials only. The tree shows a "more" icon and the
details pane the kind "Other" and no RDP profile card. What the entry is for goes in the notes (the RDM import writes
`Type: …` and the URL there).

## Consequences

* No migration. An older client reading a `Kind = 2` row treats it as not an RDP connection (it shows it as
  credentials only and could launch it); teams sharing a database should update all clients.
* A real launcher per protocol (option 2) can be added later on top of Other without changing stored data.
