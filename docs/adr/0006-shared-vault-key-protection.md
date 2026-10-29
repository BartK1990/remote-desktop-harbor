# ADR-0006: Shared vault key — DPAPI-NG to an AD group, passphrase fallback

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-21, FR-22, NFR-13

## Context

In remote mode all users must decrypt the same passwords, but the DB administrator (or anyone with a DB backup)
should not. The key must therefore be protected to "members of the team", not to one user or machine.

## Options considered

1. **SQL Server Always Encrypted** — server-agnostic for the app but SQL Server-only, needs certificate/key-vault
   distribution to every client, complicates other providers.
2. **DPAPI-NG (CNG DPAPI) with a group SID descriptor** — Windows-native; any member of an AD security group can
   unwrap on any domain-joined machine; no secret to distribute; membership managed in AD.
3. **Shared passphrase** — works anywhere (incl. non-domain), but the passphrase must be distributed and rotated.
4. Per-user public-key wrapping — strongest, but requires enrollment/approval workflow; too much for v1.

## Decision

* The shared DB's `VaultKeys.primary` row holds the DEK wrapped with **DPAPI-NG** (`SID=<group SID>`, recommended) or a
  **passphrase** (PBKDF2-SHA256, 600 000 iterations, AES-GCM). The first client to connect creates it (race-safe).
* After the first successful unwrap, the client **caches the DEK in its local cache DB, wrapped with
  `dpapi-user+machine`** — so offline mode and subsequent starts need neither a DC nor the passphrase.
* The scheme is stored with the key, so clients unwrap with the right protector regardless of their own settings.

## Consequences

* DPAPI-NG needs AD with Windows Server 2012+ DCs and a reachable DC at first unwrap.
* Removing someone from the group stops *future* unwraps but not the DEK they already cached → on staff changes,
  rotate the key (future work, see ADR-0005) and change the stored RDP passwords.
* Option 4 can be added later as another `IKeyProtector` without changing the data format.
