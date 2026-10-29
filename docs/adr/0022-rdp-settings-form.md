# ADR-0022: RDP settings edited as a form over the stored `.rdp` text

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-01, FR-05, FR-13

## Context

An RDP connection's settings were a plain text box of `.rdp` lines (`key:type:value`). Changing something as common
as the resolution meant knowing `desktopwidth:i:…`, `desktopheight:i:…` and `screen mode id:i:1`. Users asked for an
easier interface. The settings are stored as text in `Connections.RdpSettings`, shared with older clients, and
imported `.rdp` files can contain any of the hundreds of mstsc properties.

## Options considered

1. **Keep the text box, add a help link** — no work; still error-prone.
2. **Typed columns per setting** — queryable, but a migration in every provider, older clients would lose the
   settings, and every new setting is a schema change.
3. **A form over the existing text** — an OS-neutral `RdpOptions` parses the lines into typed fields and writes them
   back; lines it does not understand stay verbatim in an "Other .rdp settings" box. No schema change.

## Decision

Option 3.

* `Core/Rdp/RdpOptions` covers display (mode, resolution, colour depth, multimon, dynamic resolution, smart sizing),
  local resources (audio, microphone, keyboard hook, clipboard, printers, smart cards, all drives) and connection
  (authentication level, auto-reconnect, admin session).
* A line becomes a field only when the form can show its value exactly (right type, known value, both width and
  height in 200–8192, drives `*` or empty); otherwise it is kept as text, so no setting is lost or changed by opening
  and saving an entry.
* Field defaults are the effective launch defaults (`RdpFile.DefaultSettings`, else mstsc's own), and only values
  that differ are written; the form lines come first, the other lines after them, so a typed line still wins at launch.
* The WPF view model only maps `RdpOptions` to bindable properties (resolution presets, custom size validation).

## Consequences

* Existing entries keep working unchanged; saving one normalises its text (default-valued lines dropped, form lines
  first). Older clients see the same `.rdp` text.
* Adding a field later is a code change in `RdpOptions` only.
* Settings without a field (gateway, specific drives, …) still need the text box.
