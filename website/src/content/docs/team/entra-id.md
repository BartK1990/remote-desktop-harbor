---
title: Entra ID sign-in
description: Sign in with Microsoft Entra ID through the Windows account broker.
---

By default Harbor identifies you by your **Windows account**, with no prompt. Your display name and e-mail come from
Active Directory when it is reachable (3 s timeout), otherwise from your UPN.

Organisations that use **Microsoft Entra ID** can sign in through the Windows account broker (WAM) instead:

- silent single sign-on on Entra-joined and hybrid-joined devices,
- an interactive Microsoft sign-in otherwise.

The signed-in identity is shown in the top-right corner and is used for [presence](../session-presence/)
(“In use by …”) and for the *last changed by* audit field. It does **not** control access to the shared database or
to the stored passwords.

## 1. Register an app in Entra ID

1. In the Entra admin center go to **App registrations → New registration**.
2. Name it, e.g. *Remote Desktop Harbor*, and pick *Accounts in this organizational directory only*.
3. Under **Authentication → Add a platform → Mobile and desktop applications**, add the redirect URI:

   ```text
   ms-appx-web://microsoft.aad.brokerplugin/<client-id>
   ```

   with your application (client) ID in place of `<client-id>`.
4. Under **Authentication**, set **Allow public client flows** to *Yes*.
5. Under **API permissions**, make sure the delegated **Microsoft Graph → User.Read** permission is present.

## 2. Configure the app

**Settings → Sign-in → Microsoft Entra ID**

| Field | Value |
|---|---|
| Application (client) ID | The ID from the app registration |
| Tenant ID or domain | Your tenant ID or domain, e.g. `contoso.onmicrosoft.com`. Default: `organizations` (any work account). |

Save and restart.

## Database access is separate

Access to a shared Azure SQL database is configured in the connection string — for example
`Authentication=Active Directory Default` — independently of the app sign-in. See
[Shared database](../shared-database/#connection-string-examples).
