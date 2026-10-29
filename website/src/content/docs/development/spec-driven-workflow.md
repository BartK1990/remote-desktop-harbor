---
title: Spec-driven workflow
description: How changes are specified, decided, implemented and traced.
---

The repository is **spec-driven**. The specification is the source of truth for *what* the app does; Architecture
Decision Records (ADRs) record *why* it is built the way it is.

- Specification: [`docs/spec/SPEC.md`](https://github.com/BartK1990/remote-desktop-harbor/blob/master/docs/spec/SPEC.md)
- ADR index: [`docs/adr/README.md`](https://github.com/BartK1990/remote-desktop-harbor/blob/master/docs/adr/README.md)

## Making a change

1. **Change the spec first.** Add or modify a requirement with a stable ID — `FR-xx` (functional) or `NFR-xx`
   (non-functional). IDs are never renumbered or reused; retired requirements are marked *Retired*.
2. **Record significant decisions as ADRs.** Anything that picks a technology, data format, security mechanism or
   protocol, or is costly to reverse, gets an ADR (`docs/adr/NNNN-title.md`) from the template, listed in the ADR
   index. Superseded ADRs stay, marked *Superseded by ADR-NNNN*.
3. **Implement and trace.** Code comments and tests reference what they implement (`// SPEC FR-21`,
   `See ADR-0010`), and the traceability table in `SPEC.md` lists where each requirement lives.
4. **Review spec, ADR and code together** in one pull request.

## Rules of thumb

- Keep `Harbor.Core` and `Harbor.Data` OS-neutral; Windows APIs go in `Harbor.Windows`.
- SQL in `SqlHarborStore` stays portable; engine-specific SQL belongs in an `IDatabaseProvider`.
- Schema changes append a migration to **every** provider; never edit an applied migration.
- Never log, persist or pass on a command line any plaintext password.
- If a change affects users, update this documentation site in the same pull request.

## Requirement areas

| IDs | Area | Documentation |
|---|---|---|
| FR-01..06 | Catalog | [Managing entries](../../guides/managing-entries/), [Importing .rdp files](../../guides/importing-rdp-files/) |
| FR-10..12 | Launching Remote Desktop | [Connecting](../../guides/connecting/) |
| FR-20..25 | Storage | [Shared database](../../team/shared-database/), [Offline mode](../../team/offline-mode/) |
| FR-30..36 | Session presence | [Session presence](../../team/session-presence/) |
| FR-40..43 | Identity | [Entra ID sign-in](../../team/entra-id/) |
| NFR-10..13 | Security | [Security model](../../reference/security-model/) |
