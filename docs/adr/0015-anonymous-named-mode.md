# ADR-0015: Optional no-sign-in mode with a user-entered name

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-40..FR-44, FR-30, FR-33

## Context

Some users cannot or do not want to use Windows or Entra ID identity (e.g. a shared local account, a machine
outside the domain). The app must still work, but presence ("in use by …") and audit (`UpdatedBy`) need a name.

## Options considered

1. Require Windows or Entra ID — simple, but blocks those users.
2. Fall back silently to the Windows user name — often a meaningless shared account name.
3. A third sign-in mode where the user types a name, clearly marked as unverified.

## Decision

Option 3. `AuthMode.Anonymous` with `HarborSettings.AnonymousName` (required by the Settings dialog);
`AnonymousIdentityProvider` (OS-neutral, `Harbor.Core`) returns `UserName = name`,
`DisplayName = name + " (not signed in)"`, `IdentitySource.Anonymous`. Windows sign-in stays the default and is labelled
recommended.

## Consequences

* Anyone can type any name; presence and audit from this mode are informational only. The display name says so.
* Database access is unaffected: it is always the connection's own authentication (ADR-0009, ADR-0014).
