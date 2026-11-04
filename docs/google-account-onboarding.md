# Registration, first password change, and Google linking

## Setup

Apply `SimpleAccountRegistration.sql` once before deploying this version. It adds `users.password_changed`, marks existing users as already changed, defaults new rows to false, and adds missing email/Google identity uniqueness indexes. Duplicate existing records cause the index migration to fail; resolve them explicitly rather than deleting links automatically. Do not run the old invitation migrations for this feature. Existing invitation tables are unused and are not dropped by this change.

The existing `Onboarding` configuration section supplies `PublicUrl` (public origin), `SupportEmail`, and `AllowedWorkspaceDomains` (default `usep.edu.ph`). Its old invitation lifetime setting is unused. Brevo continues to use `Brevo:ApiKey`, `Brevo:SenderEmail`, and `Brevo:SenderName`. Keep secrets in user secrets or deployment configuration. Google's OAuth redirect remains `/signin-google`.

### Development and production

Both launch profiles select `Development`. Run `dotnet run --project CLMS.Web --launch-profile http` for `http://localhost:5124`. Local URLs and the local database connection are in `appsettings.Development.json`, which overrides shared `appsettings.json`. User secrets and environment variables can override those values.

Only `Development` accepts HTTP registration links, skips HTTPS redirection/HSTS, and allows authentication cookies over HTTP. HTTPS development URLs still work and receive Secure cookies. Google's development correlation cookie uses SameSite=Lax for its top-level GET callback. Register `http://localhost:5124/signin-google` as an authorized redirect URI in the development Google OAuth client.

Production (the default when no environment is selected) and other environments require HTTPS registration links, Secure authentication/correlation cookies, HTTPS redirection, and HSTS. The host enforces the registration URL policy after configuration binding; `Onboarding:RequireHttps=false` cannot disable HTTPS outside Development.

Shared configuration leaves deployment URLs and the database connection empty. Set `ASPNETCORE_ENVIRONMENT=Production`, `Onboarding__PublicUrl=https://<your-host>`, `AppUrl=https://<your-host>`, and `ConnectionStrings__DefaultConnection` through deployment configuration, together with JWT, Google, and Brevo secrets. For local production checks, use `--no-launch-profile` so a Development launch profile does not override the environment.

Admin is the registration authority for this MVP. SuperAdmin can replace it later. The registration service resolves the current actor from HTTP or Blazor authentication state and rechecks the actor's current database role, active status, and password-changed flag.

## Registering users

The register modal collects names, account email, role, initial password, and confirmation. Show passwords toggles both password inputs. Google linking is no longer configured during registration.

Registration saves the account immediately with a password hash and `password_changed=false`. It then sends one credentials email containing the account email, initial password, login link, strict instruction to change the password on first sign-in, and support contact. Plaintext credentials are not stored in the database, returned by the registration API, or written to logs.

If delivery fails, the account remains saved. The modal reports that the account was created but email failed, keeps the credentials visible with the password toggle, and prevents another registration submission. The administrator can record the credentials, contact the user, and close the modal. There is no mail queue or automatic delivery retry.

`POST /api/auth/register` requires an Admin Bearer token and returns **201** with `userId`, `emailSent`, and a message. It no longer logs in as the new user or returns the new user's tokens.

## First password change

Password login and Google login check the stored flag. If false, browser login issues only a ten-minute `CLMS.PasswordChange` cookie and opens `/account/change-password`. This cookie is a separate authentication scheme; it cannot authorize normal Labster pages. No regular application cookie is issued until a subsequent login with a changed password.

The password page requires the current password, a different new password of at least eight characters, and matching confirmation. Saving updates the hash and flag together, only when the active account's previous hash still matches. A stale profile edit cannot overwrite a changed password. Refresh tokens are revoked after password changes.

Success clears the browser's ordinary/restricted login cookies and returns to login with a success message. Users then sign in again. Already registered users can also change their password through Account settings. The flag changes only after a password change, never simply after logging in.

JWT login and refresh return **403** with `code: "password_change_required"` and `passwordChangeUrl` while the flag is false. API users must sign in through the browser, change the password, and then retry API login. No ordinary tokens are issued in the meantime.

## Google sign-in

1. The Google middleware obtains the provider subject, email, verified-email claim, and Workspace hosted domain. Organization email and hosted domain must match an allowed domain.
2. Look up the Google subject in `external_identities` first. A linked active account proceeds to normal login or required password change. Disabled or missing linked accounts are rejected without email fallback.
3. For an unlinked subject, find an active internal account by normalized Google email. No email match means the user is not registered; no user account is created automatically.
4. A matching email opens `/auth/google/link`, asking for the Labster account password and explicit confirmation. Google claims remain in the protected five-minute external cookie; user IDs, emails, or subjects submitted in form fields cannot select another account.
5. After verifying the password, linking rechecks the active account, email and password hash under a database lock. Unique constraints prevent another Google subject/account assignment. Existing links are never replaced, and repeated concurrent confirmation cannot create duplicate links.
6. Successful linking clears the external cookie and applies the same first-password-change rule as ordinary login. Cancellation clears the external cookie and returns to login.

The account screens are static Razor components in `Components/Pages/Account`, wrapped by `Components/Layout/AccountLayout.razor`. AccountController renders them directly with RazorComponentResult, keeping the existing GET/POST URLs and cookie handling. They use ordinary HTTP forms with AntiforgeryToken components, without an interactive circuit.

Link and password forms use MVC antiforgery validation, no-store responses, and login rate limiting. The Google middleware state/correlation protection remains in place. `AddControllersWithViews()` registers the antiforgery filter services needed by these forms.

## Verification

```powershell
dotnet build CLMS.slnx
dotnet run --project tests/CLMS.Onboarding.Tests
dotnet run --project tests/CLMS.Onboarding.Tests -- --environment-only
dotnet run --project tests/CLMS.Onboarding.Tests -- --mysql
```

If the development app holds output DLLs open, use `--artifacts-path tests/CLMS.Onboarding.Tests/bin/verification` when building the test project and run the produced executable from that directory. This leaves the running app untouched.

The suite verifies registration authorization, initial credentials email, email-failure outcome, flag enforcement, password validation, protected Google linking, and real HTTP/MVC/cookie/antiforgery behavior. MySQL checks use a newly created `clms_account_test_<random>` database, run the migration there, and drop only that disposable database afterward. Set `CLMS_TEST_MYSQL` for a test-capable connection string; otherwise the suite uses server credentials from local appsettings. It never migrates the application's database or sends real email.

Actual Brevo delivery and interactive Google consent require their configured external services and are not performed by the regression suite.
