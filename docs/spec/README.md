# Specifications

This repository is **spec-driven**. The documents here are the source of truth for *what* the app does;
[Architecture Decision Records](../adr/README.md) record *why* it is built the way it is.

## Workflow

1. **Change the spec first.** Any change in behaviour starts as an edit to [`SPEC.md`](SPEC.md):
   add or modify a requirement with a stable ID (`FR-xx` functional, `NFR-xx` non-functional).
   Never reuse a retired ID; mark it *Retired* instead.
2. **Record significant decisions as ADRs.** If the change picks a technology, a data format, a security
   mechanism, a protocol or anything costly to reverse, add an ADR (`docs/adr/NNNN-title.md`) from the
   [template](../adr/0000-template.md). Superseded ADRs stay in place with status *Superseded by ADR-NNNN*.
3. **Implement and trace.** Code comments and tests reference the requirement or ADR they implement
   (`// SPEC FR-21`, `See ADR-0010`). The traceability table in `SPEC.md` lists where each requirement lives.
4. **Review spec + ADR + code together** in one pull request.

## Documents

| Document | Content |
|---|---|
| [SPEC.md](SPEC.md) | Product requirements, data model, security model, traceability |
| [../adr/](../adr/README.md) | Architecture decision records |
