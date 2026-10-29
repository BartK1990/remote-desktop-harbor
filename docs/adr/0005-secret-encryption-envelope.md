# ADR-0005: Secret encryption — AES-GCM envelope, key bound to user + machine with DPAPI

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-04, NFR-10, NFR-11, NFR-12

## Context

Passwords must be encrypted by the app and bound to the OS user/machine. In shared mode the same ciphertext must be
readable by teammates, so encrypting each password directly with DPAPI (user-specific) does not work there.

## Decision

**Envelope encryption** used identically in all stores:

* A random 256-bit data-encryption key (DEK) per vault encrypts each password with **AES-256-GCM**.
  Envelope v1: `[version][keyId 16][nonce 12][tag 16][ciphertext]`. The entry id is **associated data**, so a
  ciphertext moved to another row or modified fails authentication. The key id detects a wrong key early.
* The DEK is stored *wrapped* in the store's `VaultKeys` table by an `IKeyProtector`, identified by a scheme name:
  * `dpapi-user+machine` (default for local stores): DPAPI CurrentUser, then DPAPI LocalMachine →
    only this Windows user on this computer can unwrap. No admin rights needed.
  * `dpapi-user`: CurrentUser only (roams with the profile) — available, not default.
  * `dpapi-ng`, `passphrase`: for shared databases (ADR-0006).
* Plaintext is held only for the moment of use; buffers are zeroed where the API allows.

## Consequences

* Copying a local database to another machine/user yields unreadable passwords (intended).
* Windows profile reset / password reset by an admin can make DPAPI keys unrecoverable → local-mode users should keep
  a backup elsewhere or use a shared vault.
* Key rotation (new DEK, re-encrypt all) is future work; the key id in the envelope makes it possible.
