# Web login and password recovery

Users can enable or disable email two-factor authentication at **Account settings** (`/account/settings`). Enabled accounts enter a six-digit email code after password or Google sign-in. Agent device authentication is separate.

The preference adds one column, `users.two_factor_enabled`, defaulting to **true** for existing and newly created accounts. Stop the web application, back up the database, and apply `Database/migrations/2026-11-09-user-two-factor-preference.sql` **once before deploying the updated build**. Fresh databases use the updated schema export instead of this migration. The migration is not applied automatically. Existing human sessions must sign in again because the preference is now part of their credential fingerprint.

## Configuration

Keep the existing `Brevo:ApiKey`, `Brevo:SenderEmail`, and `Brevo:SenderName` settings in deployment secrets. Brevo needs outbound connectivity from the web server. Set `Onboarding:PublicUrl` to the browser-accessible URL, for example `https://labster.example`, rather than an address accessible only from the server. Reset links use this setting, never the request Host header.

Production account routes require HTTPS. The existing Development HTTP launch profile is supported when `Onboarding:PublicUrl` is HTTP; do not bypass certificate validation for HTTPS. A TLS-terminating reverse proxy must have trusted forwarded headers configured before the HTTPS check. Disable reset-query logging in proxy/access logs and telemetry. The application suppresses request-URL and controller-argument logging for credential confidentiality.

Keep `Jwt:Key` stable and secret. A purpose-separated HMAC derived from that existing key creates a fingerprint of the current password hash, email, role, initial-password state, and 2FA preference. Cookies and human JWTs contain the fingerprint, never the password hash. Resetting/changing the password or changing the preference invalidates old sessions. Configure a persistent, access-controlled ASP.NET Core Data Protection key ring for your web hosting identity so completed cookie sessions survive deployments. This uses the existing cookie framework rather than introducing an additional credential vault.

The schema export also includes `failed_login_attempts` and `lockout_end`, which are already present in the live database. Only the new 2FA-preference column requires migration.

## Behavior and limits

- Account settings displays the saved status and an Enable/Disable action. Requesting a change sends a purpose-bound email code. Saving requires both the current password and that code, with a shared five-attempt budget. Settings proofs cannot authenticate a login or change another account. The target preference is held on the server, not taken from the confirmation form.
- Saving atomically updates the preference, rehashes the same confirmed password with a fresh salt, clears outstanding reset proofs, and revokes human refresh tokens. The password itself is unchanged. Rehashing permanently invalidates old fingerprints, even when the preference is later toggled back. **Disabling keeps the current browser signed in** by renewing its cookie with the exact saved fingerprint and its original expiration, persistence, and Remember Me behavior. Other browser sessions and old JWTs/refresh tokens remain revoked. Enabling continues to sign out the current browser. Ordinary profile edits do not overwrite the security setting.
- Cancellation, expired/incorrect proofs, failed delivery, and database rollback leave the preference unchanged. Pending confirmations are temporary in-memory state; the saved preference survives web restarts and browser changes.
- Codes expire after five minutes and permit five failed attempts. They are consumed atomically. The pending cookie has a separate scheme, no roles, and no normal application access.
- Resend waits 60 seconds, replaces the code, and preserves the original expiration and remaining attempts. Five sends per account per 15 minutes are allowed across new login attempts and resends.
- A failed verification-email delivery produces a retryable sign-in error and no session. “Remember Me” applies after verification and never skips it.
- The existing initial-password-change restriction follows email verification. Changing that password signs the user out and requires a fresh login.
- Forgot password queues the normalized address before looking up the account. Unknown, deleted, and eligible addresses have the same confirmation and limits; no delivery or registration status is returned. The queue is limited to 100 addresses. If delivery fails, request another link after the cooldown.
- Reset-link requests allow **one per normalized email address every 60 seconds**, with a maximum of **five per address in a rolling 15 minutes** and **five per IP in 15 minutes**. Blocked requests return HTTP `429` with an accurate `Retry-After`. The browser stays on the forgot-password form and retains the submitted address. During cooldown the button is disabled and gray, with a clock icon and a live **MM:SS** label (for example, **Send in 01:00**). It returns to **Send reset link** when the timer ends. Longer throttling windows use their actual remaining time. Exceptionally large or chunked bodies rejected by the IP limiter are not parsed merely to echo their address.
- The countdown survives refresh through a deadline in `sessionStorage`; no email address, password, or reset token is stored there. Server-response timing replaces stale browser timing. Editing/deleting storage, opening another browser, or disabling JavaScript does not bypass the server limits. Counters are in memory and reset when the single web instance restarts.
- Reset links contain 256 random bits, expire after 30 minutes, and work once. Only a 64-character SHA-256 hash is stored in `users.reset_token`; expiry uses `reset_token_expiration`.
- Reset passwords must contain **8–1024 characters**, match their confirmation, and differ from the current password. The browser validates length and the server enforces all three checks. Validation errors leave the reset proof usable and do not revoke sessions. Password-reuse checks require a valid reset proof.
- Reset consumption, password update, `password_changed`, and human refresh-token revocation commit in one MySQL transaction. Consumption requires the password hash that was checked, preventing a concurrent password change from bypassing reuse validation. New requests replace old tokens. Password or email changes clear old reset proofs. Conditional issuance prevents sending a valid proof for an address that was changed concurrently.
- Reset does not sign the user in automatically and preserves the saved 2FA preference. Email verification is required afterward only when enabled.
- Cookies and human JWTs check the current fingerprint and active account on HTTP authentication. Existing Blazor circuits revalidate every 30 seconds, and `CurrentUser.RequireAdminAsync` also validates current credentials and role.
- Human refresh rotation and resets use the same user-row lock, allowing only one refresh successor and preventing a stale account snapshot from issuing credentials. New stored refresh hashes have a `human2fa:v1:` prefix; legacy refresh tokens cannot bypass the new code gate. Agent token hashing is unchanged.

The login page uses two columns at widths of **1024px and above**. On smaller screens the form appears first, followed by the primary-colored SVG background panel with a minimum height of **16rem**. The form width adapts to the viewport.

This is a **single web instance** design. Challenges, send limits, and queued addresses are bounded in-memory state. Restarting the web server cancels pending verification/recovery/settings-confirmation work; users retry. Saved preferences and already-issued reset links persist in MySQL. No durable outbox, distributed cache, TOTP, trusted-browser exemption, or new security-stamp column is introduced. Mailbox recovery remains an administrator/support matter.

## Routes

| Route | Methods | Purpose |
| --- | --- | --- |
| `/forgot-password` | GET, POST | Request an emailed reset link |
| `/reset-password` | GET, POST | Set a new password using the link |
| `/auth/two-factor` | GET, POST | Verify a pending browser login |
| `/auth/two-factor/resend` | POST | Send a replacement code |
| `/auth/two-factor/cancel` | POST | Cancel the pending login |
| `/account/settings` | GET | View and request a preference change |
| `/account/two-factor/request` | POST | Email a settings-confirmation code |
| `/account/two-factor/confirm` | POST | Confirm using the current password and code |
| `/account/two-factor/resend` | POST | Resend a settings-confirmation code |
| `/account/two-factor/cancel` | POST | Cancel the proposed settings change |

Browser POST forms require antiforgery tokens. Normal login and Google callback enter the same completion gate.

`POST /api/auth/login` returns **200** with the existing access/refresh-token response when 2FA is disabled. When enabled, correct credentials return **202**:

```json
{
  "code": "two_factor_required",
  "challengeCredential": "opaque temporary secret",
  "expiresAt": "UTC timestamp",
  "resendAfter": "UTC timestamp"
}
```

Call `POST /api/auth/two-factor/verify` with `{ "challengeCredential": "...", "code": "123456" }` to obtain the existing access/refresh-token response. Call `POST /api/auth/two-factor/resend` with `{ "challengeCredential": "..." }` to resend. API clients must handle this new intermediate response. Refresh tokens remain opaque to clients. Accounts needing an initial password change receive `403 password_change_required` after verification and must use the existing browser flow.

## Testing

Run `dotnet test CLMS.Tests/CLMS.Tests.csproj`. Portable tests cover expiry, replay, concurrent code consumption, resend/attempt limits, process restarts, queue bounds, cryptography, and stale administrator sessions.

Run `node --test CLMS.Tests/AccountSecurityCountdown.test.mjs` for countdown labels, refresh persistence, disabled-storage, and verification-resend checks. Server tests also exercise concurrent recovery requests, rolling-window boundaries, IP rejections, cookie persistence/expiration, stale-renewal rejection, password reuse, length boundaries, and preservation of reset proofs after validation failures.

Set `CLMS_TEST_MYSQL` to a test connection with create/drop-database permission, or opt into the existing local test configuration with `CLMS_REGISTRY_INTEGRATION=1`, for transactional and HTTP integration tests. These create uniquely named `clms_account_test_*` databases and delete only their own database. Real email is replaced by a test sender.

For a deployment smoke test, verify password and Google login with actual Brevo delivery, wrong/expired codes, resends, and a reset link opened from another workstation. Disable 2FA through settings and confirm that the current browser stays signed in while another browser session and old tokens fail. Enable it again and check the sign-out and code gate. Verify the reset-link timer, refresh it, and try an immediate second request to confirm the server cooldown. Try the current password and an invalid length before completing the same reset link with a valid different password. Check login at narrow and desktop widths. Apply the existing preference migration before the first deployment of preference support; these validation, cookie, and UI changes require no additional migration.
