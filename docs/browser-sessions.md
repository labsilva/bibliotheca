# Browser and PWA sessions

The login form already submitted the **Manter sessão iniciada** checkbox. The API ignored that value and issued a session cookie for both choices. The frontend kept its OIDC state in `sessionStorage`, which a newly opened window starts without, and had no refresh flow when the access token expired. Reopening an app window could therefore lose the frontend session, and reopening a browser without the original cookie required another password sign-in.

| Login choice | API cookie | Session lifetime |
| --- | --- | --- |
| Checked | Host-only `idp_auth` with a 14-day expiry | 14 days from password sign-in |
| Unchecked | Host-only `idp_auth` for the browser session | Up to 8 hours from password sign-in, or until the browser discards the session cookie |

The API sends the cookie with `HttpOnly`, `SameSite=Lax` and `Secure` in production. The frontend keeps its access and refresh tokens in `sessionStorage`. A fresh browser or PWA window starts a top-level OIDC authorization redirect. If that window can access a valid API cookie, the redirect returns to the requested page without a password prompt. While a window is open, the frontend uses a rotating refresh token to renew access after expiry or resume. Access tokens last up to 15 minutes. Renewal keeps the original session deadline. Signing out or signing in as another account with the same browser cookie revokes the prior grant, including its access and refresh tokens. Sessions in independent browser profiles remain independent.

Cookie clearing, private browsing and browser storage policies can end the session earlier. Some browsers restore session cookies when configured to restore the previous session. A PWA uses the cookie store available to its window, which can be separate from a regular browser tab on some devices. Restoring a session in a fresh window requires a connection to the API. Users with cookies issued by an older release may need to sign in again once after the upgrade.

## Deployment verification

Keep the SQLite database, the Data Protection key directory and both OIDC certificates on persistent storage across API restarts. Keep `Authentication:Issuer` and `Authentication:FrontendOrigin` aligned with the HTTPS origins that users see. If a trusted proxy terminates TLS, verify forwarded scheme and host. [OIDC certificate configuration](certificates.md#oidc-certificates)

1. With a test account on the published site, check **Manter sessão iniciada** and sign in. Inspect the `idp_auth` cookie metadata and confirm that it has a 14-day expiry, `HttpOnly`, `Secure`, `SameSite=Lax`, path `/` and the API host. Inspect the metadata without recording the cookie value or tokens.
2. Quit and reopen the desktop browser and an installed mobile PWA. Open a protected page in each. Restart the API with the same storage and certificates and repeat. Leave a window open beyond 15 minutes and check that the next request renews the token without a password prompt.
3. Repeat with the option unchecked and a browser profile configured to discard session cookies when it quits. Check that reopening prompts for the password. Verify logout and a change of account in two windows of the same profile, including that the previous account's private books and settings are unavailable in the other window.

The integration tests cover cookie attributes, renewal, revocation, account switching and an API restart using production settings. The Chromium browser tests exercise the OIDC navigation and token refresh with a fixture identity provider. Complete the checks above against the published deployment and an installed app on each supported mobile platform.
