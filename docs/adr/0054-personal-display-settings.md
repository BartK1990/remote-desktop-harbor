# ADR-0054: Monitors, and optionally display and local resources, are kept per user and PC

* Status: Accepted
* Date: 2026-11-25
* Requirements: FR-76, FR-75, FR-13

## Context

An entry's `.rdp` settings are part of the entry, so with a shared database every user connects with the same ones.
Some of them describe the user's own PC rather than the server: which monitors a full screen session covers (FR-75)
uses monitor numbers that only mean something on one PC, and full screen or window, resolution and scaling are often a
matter of the user's screens. Likewise, whether sound plays here, the microphone is used and which local drives,
printers, clipboard and smart cards are shared depend on the user's PC. And Remote Desktop's monitor numbers are not even stable
on one PC: they are the Windows display device numbers less one (`\\.\DISPLAY5` = 4), which change when a monitor
is plugged into another port or dock (a PC listed its monitors as 0, 4 and 5).

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
display group personal (mode, size, several monitors, monitors, dynamic resolution, smart sizing), and **Use my own
local resources on this PC** the local resources group (`audiomode`, `audiocapturemode`, `drivestoredirect`,
`redirectprinters`, `redirectclipboard`, `redirectsmartcards`; `OwnResources`). Monitors are kept **by position and
size** (`MonitorRef`), not by number, and matched to this PC's monitors on every connection (same bounds, else the same
size nearest to where it was), which then gives the `selectedmonitors` numbers of the moment. At launch the entry's lines of a personal group are dropped and the personal ones used, so a default
(e.g. full screen at the local size, no drives) needs no line at all. Everything else stays in the entry. The editor
shows the merged settings and splits them again on save; the entry's own lines of those groups are left as they were.

## Consequences

* Each user picks their own monitors and display for a shared entry, without changing it for the team.
* Personal settings do not follow the user to another PC and are not in a backup.
* Entry ids are the keys: a moved entry (new ids, ADR-0038) has its settings re-keyed, a deleted one forgets them.
