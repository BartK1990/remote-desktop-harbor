# ADR-0054: Monitors, and optionally all display settings, are kept per user and PC

* Status: Accepted
* Date: 2026-11-25
* Requirements: FR-76, FR-75, FR-13

## Context

An entry's `.rdp` settings are part of the entry, so with a shared database every user connects with the same ones.
Some of them describe the user's own PC rather than the server: which monitors a full screen session covers (FR-75)
uses monitor numbers that only mean something on one PC, and full screen or window, resolution and scaling are often a
matter of the user's screens.

## Options considered

1. **Keep everything in the entry** — simple, but one user's monitor choice moves another user's session to the wrong
   screens.
2. **Per-user columns in the database** — shared mode only, needs a schema change for every provider, and does not
   follow the user from PC to PC anyway.
3. **Personal settings in the user's settings file, by entry id** (like pins and the manual order, ADR-0028, ADR-0052) —
   no schema change, works in every mode and offline, and is naturally per PC.

## Decision

Option 3. `HarborSettings.PersonalRdp` maps an entry id to `PersonalRdpSettings` (`OwnDisplay`, `.rdp` lines). The
monitors line (`selectedmonitors`) is always personal. **Use my own display settings on this PC** makes the whole
display group personal (mode, size, several monitors, monitors, dynamic resolution, smart sizing): at launch the
entry's display lines are dropped and the personal ones used, so a default (full screen at the local size) needs no line
at all. Everything else stays in the entry. The editor shows the merged settings and splits them again on save; the
entry's own display lines are left as they were.

## Consequences

* Each user picks their own monitors and display for a shared entry, without changing it for the team.
* Personal settings do not follow the user to another PC and are not in a backup.
* Entry ids are the keys: a moved entry (new ids, ADR-0038) has its settings re-keyed, a deleted one forgets them.
