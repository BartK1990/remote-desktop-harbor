# ADR-0035: Hand the password over as a generic `TERMSRV/<host>` credential

* Status: Accepted
* Date: 2026-11-04
* Requirements: FR-10, FR-11, NFR-12
* Amends: ADR-0008

## Context

ADR-0008 writes the password into Credential Manager as a *domain-password* credential (`CRED_TYPE_DOMAIN_PASSWORD`).
On machines with Windows Defender Credential Guard, Remote Desktop refuses saved domain credentials: mstsc shows
"Your credentials did not work — Windows Defender Credential Guard does not allow using saved credentials" and asks
for the password. Credential Guard is on by default on many current Windows 11 Enterprise machines, so most users of
a managed fleet hit this on every connect. Other tools (e.g. Remote Desktop Manager) connect without a prompt there.

Credential Guard protects *domain* credentials; a *generic* credential (`CRED_TYPE_GENERIC`) with the same
`TERMSRV/<host>` target is still read by mstsc and passed to CredSSP as supplied credentials. This is the same effect
as the well-known `cmdkey /generic:TERMSRV/<host>` workaround, without the password on a command line.

## Options considered

1. Keep the domain-password credential; users paste the password — status quo, a prompt on every connect.
2. Embedded ActiveX client with `ClearTextPassword` — rejected in ADR-0008 (sessions must be normal mstsc windows).
3. **Write a generic `TERMSRV/<host>` credential via `CredWrite`.**

## Decision

Option 3. `WindowsCredentialStore.WritePassword` writes `CRED_TYPE_GENERIC` with the same target, user name and
persistence as before (Session by default, Local Machine with "keep credential"). Before writing, it deletes a
domain-password credential for the same target — mstsc would prefer it and Credential Guard would then reject it.
`Delete` removes both types, so credentials left by older versions are cleaned up too.

## Consequences

* No password prompt under Credential Guard; behaviour without Credential Guard is unchanged.
* A generic credential's blob can be read back (`CredRead`) by any process of the same Windows user, whereas a
  domain-password blob is only readable by LSA. Such a process could already read the app's decrypted vault in
  memory, and the credential lives ~30 s and never survives logoff by default, so the exposure is accepted.
  NFR-12 still holds: nothing on disk in plaintext, nothing on a command line.
* A credential the user saved themselves with mstsc "Remember me" for the same host is replaced, as before.
* The "Unknown remote connection" warning mstsc shows for unsigned `.rdp` files is a separate prompt and is not
  affected by this decision.
* The *Allow delegating saved credentials* group policy, when it denies the server, can still cause a prompt.
