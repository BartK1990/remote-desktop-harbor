---
title: Security model
description: How passwords are encrypted, where the keys live and which threats are addressed.
---

## Overview

```
                      ┌──────────────── shared SQL DB ─────────────────┐
 password ─AES-GCM──► │ PasswordCipher (DEK)   VaultKeys: DEK wrapped  │
                      │                        with DPAPI-NG (AD group)│
                      │                        or passphrase           │
                      └───────────────────────────┬────────────────────┘
                                                  │ sync (ciphertext only)
                      ┌──────────── local SQLite (cache / vault) ──────┐
                      │ PasswordCipher (same DEK)  VaultKeys: DEK      │
                      │                            wrapped with DPAPI  │
                      │                            user + machine      │
                      └────────────────────────────────────────────────┘
```

## Envelope encryption

- Each vault has one random **256-bit data-encryption key (DEK)**.
- Every password is encrypted with **AES-256-GCM** under the DEK. Envelope format v1:
  `[version][keyId 16][nonce 12][tag 16][ciphertext]`.
- The **entry id is authenticated associated data** (for an entry's additional accounts, the account id): a
  ciphertext copied to another row or account, or modified in any way, fails to decrypt.
- The key id detects a wrong key early and makes future key rotation possible.
- Passwords are decrypted only at the moment of use (connect, copy); buffers are zeroed where the API allows.

## Key protection

The DEK is never stored in plain form. It is wrapped by a *key protector*, whose scheme is stored with the key:

| Scheme | Used for | Who can unwrap |
|---|---|---|
| `dpapi-user+machine` | Local vault and every local cache (default) | Only this Windows user on this computer (DPAPI CurrentUser, then LocalMachine) |
| `dpapi-user` | Available, not default | This Windows user (roams with the profile) |
| `sign-in` | Shared database (default) | Anyone who can sign in to and read the database (Windows, SQL login or Entra ID; any app sign-in) — including DBAs and backups |
| `dpapi-ng` | Shared database (strongest) | Members of an AD security group |
| `passphrase` | Shared database without AD | Anyone with the passphrase (PBKDF2-SHA256, 600 000 iterations) |

See [Vault key protection](../../team/vault-key-protection/).

## Handing the password to Remote Desktop

The password reaches `mstsc.exe` through Windows Credential Manager (`TERMSRV/<host>`, session persistence by
default), never through a command line (visible in the process list) or an `.rdp` file. See
[Connecting](../../guides/connecting/).

## Threats addressed

| Threat | Mitigation |
|---|---|
| Stolen laptop disk or copied database file | DEK bound to Windows user + machine (DPAPI) |
| Database administrator reading passwords | Shared database holds ciphertext only; the DEK is wrapped to the team |
| Row tampering or swapped ciphertexts | AES-GCM authentication with the entry id as associated data |
| Password leakage via process list or `.rdp` files | Credential Manager hand-off; password keys stripped from `.rdp` files |
| Password left on the clipboard | Cleared after 30 s if unchanged |
| Password in logs or settings | Never logged; connection string DPAPI-protected |

## Out of scope / accepted risks

- A user who can unlock a shared vault can read every password in it — that is its purpose.
- Malware running as the same Windows user.
- Key rotation is future work; removing someone from the AD group does not revoke a key they already cached.
- A Windows profile reset or administrator password reset can make DPAPI-protected local keys unrecoverable.
