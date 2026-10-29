# ADR-0016: Shared vault key protected by verified sign-in (no passphrase); one-time conversion

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-28, FR-22, FR-44, NFR-13
* Amends: ADR-0006
* Amended by: ADR-0018 (no app sign-in required)

## Context

Teams found the shared passphrase a burden: it must be distributed and typed on each machine. They already
authenticate twice — to Windows / Entra ID (app sign-in) and to the shared database — and want that to be enough.
DPAPI-NG to an AD group removes the passphrase but needs AD group setup and does not cover Entra-only users.
Existing vaults were created with a passphrase and must be movable without re-encrypting entries.

## Options considered

1. Keep passphrase / DPAPI-NG only — secure, but the requested workflow is not possible.
2. Derive the key from the Entra token or Windows credentials — not shareable between users without a server.
3. Store the key in the shared database, released by the app only to users with a verified Windows or Entra ID
   sign-in — no passphrase, works for domain and Entra users; protection = database access control.

## Decision

Option 3 as scheme `sign-in` (`SignInKeyProtector`, OS-neutral), the new default for new vaults. DPAPI-NG and passphrase
remain as stronger choices.

* The key is stored unencrypted (`[version][key]`) in `VaultKeys`; the app refuses to wrap/unwrap it for an
  anonymous identity (FR-44). This check runs in the client: it is a usability guard, not a security boundary.
* **Conversion:** `HarborSettings.RemoteKeyConvert` is a one-time request. On the next successful connection the
  workspace re-wraps the existing key with the configured scheme (same `KeyId`, `ReplaceVaultKeyAsync`), then the
  app clears the request. It uses the key cached on that machine, so a client that opened the vault before needs no
  passphrase; other clients' cached copies stay valid because the key does not change.

## Consequences

* **Security trade-off (NFR-13):** with `sign-in`, anyone who can read the shared database or a backup of it — including
  a DBA — can read every stored password. Settings states this next to the option. Teams that need DBA separation
  choose DPAPI-NG (or passphrase).
* Converting from `sign-in` back to DPAPI-NG/passphrase is possible the same way, but anyone who read the database
  meanwhile may have kept the key; rotate the key/passwords in that case (key rotation is future work, ADR-0005).
