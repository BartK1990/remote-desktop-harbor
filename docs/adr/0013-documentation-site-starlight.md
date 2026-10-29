# ADR-0013: Documentation site — Astro Starlight on GitHub Pages

* Status: Accepted
* Date: 2026-10-29
* Requirements: —

## Context

Users and admins need task-oriented documentation (install, connect, team setup, security) beyond the README.
The spec and ADRs describe *what* and *why* for contributors, not *how to use* the app.

## Options considered

1. **README + Markdown in `docs/`** — no tooling, but no navigation, search or user-oriented structure.
2. **DocFX** — .NET-native, strong for API reference, which we do not need; heavier theme customisation.
3. **Astro Starlight** — Markdown pages, sidebar navigation, built-in full-text search, light/dark theme, static
   output that deploys to GitHub Pages.

## Decision

Starlight, in `website/` (Node.js, separate from the .NET solution). Every page, including the start page, uses the
regular doc layout (no splash page) so the navigation sidebar is always visible. The sidebar is defined explicitly in
`website/astro.config.mjs`. `.github/workflows/docs.yml` builds the site on PRs that touch `website/` and deploys it
to GitHub Pages from `master`.

## Consequences

* User-facing behaviour changes should update the matching page in the same PR as the spec and code.
* The spec (`docs/spec/SPEC.md`) and ADRs stay the source of truth; the site links to them instead of copying them.
* Node.js is needed only to work on the docs, not to build or test the app.
* One-time setup: repository Settings → Pages → Source = GitHub Actions.
