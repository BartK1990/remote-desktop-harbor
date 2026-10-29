# ADR-0001: Spec-driven development with ADRs

* Status: Accepted
* Date: 2026-10-29

## Context

The app handles credentials and shared state for a team. Behaviour and security properties must be explicit and
reviewable, and the reasons behind early choices (framework, crypto, storage) must survive team changes.

## Decision

* `docs/spec/SPEC.md` is the source of truth for behaviour, with stable requirement IDs (`FR-xx`, `NFR-xx`).
* Significant decisions are recorded as ADRs in `docs/adr/` (Michael Nygard format, template `0000-template.md`).
* Code and tests reference requirement/ADR IDs; `SPEC.md` keeps a traceability table.
* A behaviour change is one PR containing spec change, ADR (if needed), code and tests.

## Consequences

Slightly more writing per change; in exchange reviewers can check code against intent, and security-relevant
decisions are documented for audits.
