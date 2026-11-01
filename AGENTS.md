# Agent guide

Spec-driven repo. Before changing behaviour:

1. Read `docs/spec/SPEC.md` and the relevant ADRs in `docs/adr/`.
2. Update the spec (requirement IDs are stable — never renumber or reuse) and add an ADR for significant decisions
   (`docs/adr/0000-template.md`, add it to `docs/adr/README.md`).
3. Implement; reference IDs in comments/tests (`// SPEC FR-31`, `See ADR-0010`); update the traceability table.

Build/test (works on Linux thanks to `EnableWindowsTargeting`; the WPF app only runs on Windows):

- `dotnet build RemoteDesktopHarbor.slnx` — warnings are errors.
- `dotnet test RemoteDesktopHarbor.slnx` — `Harbor.App.Tests` (WPF + DPAPI) only runs on Windows; it is skipped
  elsewhere, so keep logic that can be OS-neutral out of `Harbor.App`.
- Docs site (`website/`, Astro Starlight, Node 22.23+): `npm ci && npm run build`. Update the matching page when
  user-facing behaviour changes.

Rules:

- Keep `Harbor.Core` and `Harbor.Data` OS-neutral (`net10.0`); Windows APIs go in `Harbor.Windows`.
- SQL in `SqlHarborStore` must stay portable; engine-specific SQL belongs in an `IDatabaseProvider`.
- Schema changes = append a migration to *every* provider's `Migrations` list; never edit an applied migration.
- Never log, persist or pass on a command line any plaintext password.
- Package versions live in `Directory.Packages.props`.
