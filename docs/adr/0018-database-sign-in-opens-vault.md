# ADR-0018: Database sign-in alone opens a `sign-in` vault (no app sign-in required)

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-28, FR-43, FR-44, NFR-13
* Amends: ADR-0016

## Context

ADR-0016 let a `sign-in`-protected shared vault open only for users signed in to the app with Windows or Entra ID;
users in no-sign-in mode (FR-44) were refused, and Settings forced them to a passphrase or AD group. Some users
connect to the shared database with a SQL login (user + password) from a Windows account that has no database access
of its own, and do not use an app sign-in. They have already authenticated to the database and expect that to be
enough. ADR-0016 itself notes that the app-side check is a usability guard, not a security boundary: the key is
stored in the database as is.

## Options considered

1. Keep the guard — those users must distribute and type a passphrase although database access already exposes the key.
2. Allow it only for SQL logins — arbitrary: Windows-integrated and Entra database sign-ins authenticate just as well.
3. Drop the app sign-in requirement: whoever can sign in to the shared database opens the vault.

## Decision

Option 3. `SignInKeyProtector` no longer depends on the app identity; the Settings dialog no longer rejects
*No sign-in* combined with this protection, which it now labels "Database sign-in". The scheme name stays `sign-in`
so existing vaults keep working.

## Consequences

* Protection is exactly the database's access control (unchanged in substance, see NFR-13): anyone who can read the
  database or a backup of it can read the stored passwords.
* App sign-in only identifies the user for presence and audit (FR-42), independent of vault access.

## Note (2026-10-29)

The Settings label "Database sign-in" read like a protection mechanism. Since the key is stored unencrypted, the
option is now shown as **"Not protected – key stored unencrypted in the database"**. Scheme name and behaviour are
unchanged.
