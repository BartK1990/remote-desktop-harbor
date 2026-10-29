# ADR-0009: Identity — Windows account by default, Entra ID via WAM broker

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-40..FR-43

## Context

The app must know who the user is (presence: user name + e-mail, audit) and support Windows domain accounts and
Microsoft Entra ID, ideally with single sign-on.

## Decision

* `IIdentityProvider` with two implementations:
  * **Windows** (default): `WindowsIdentity.GetCurrent()` — no prompt, works offline. Display name/UPN from
    `GetUserNameEx`; e-mail from AD (`UserPrincipal.EmailAddress`) with a 3 s timeout, fallback UPN.
  * **Entra ID**: MSAL.NET with the **WAM broker** — silent sign-in with the Windows account
    (`OperatingSystemAccount`) on Entra/hybrid-joined devices, interactive otherwise. Claims `name`, `email`,
    `preferred_username`. Needs an app registration (public client) with redirect URI
    `ms-appx-web://microsoft.aad.brokerplugin/{clientId}` and `User.Read`.
* Authorization to the shared DB is **not** derived from the app identity; it is the SQL connection's own
  authentication (`Integrated Security` or `Authentication=Active Directory Default/Interactive`), so access control
  stays with the DBA.

## Consequences

* App sign-in is about identity, not about protecting local data (that is DPAPI's job, ADR-0005).
* Future: use the Entra token for Azure SQL access tokens to have a single prompt.
