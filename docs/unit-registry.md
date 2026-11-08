# Unit Registry and persistent device authentication

The administrator page is `/admin/unit-registry`. It reuses administrator cookies,
active-administrator checks, confirmation dialogs, toasts, and the registry use cases.
Agents use the separate `AgentBearer` scheme, `CLMS.Agent` audience, and `AgentOnly`
policy. Human authentication and human refresh tokens are unchanged.

An approved device uses a persistent ECDSA P-256 private key to obtain a 15-minute
JWT. There are **no agent refresh tokens and no inactivity expiration**. A computer
can remain powered off for months and authenticate after restarting, provided its
key survives and its credential/unit is still active.

## Deployment and the existing registered device

1. Back up the database and stop CLMS.Web during the schema change. Select the CLMS
   database and apply `Database/migrations/2026-11-08-persistent-device-authentication.sql`
   once, using the MySQL client without `--force`. It upgrades the current Unit Registry
   schema; it is not a migration for a pre-registry database. MySQL DDL commits
   implicitly, so an interrupted migration must be inspected before retrying.
2. Deploy the updated server and service/UI together. Existing agent refresh records
   are revoked but retained for history; human `refresh_tokens` are untouched.
   Legacy pending registrations without public keys become Expired. The current
   schema exports in `Database/clms` include the new tables and registration columns;
   import `device_credentials` before `unit_registrations` when creating a fresh schema.
3. Soft-delete the existing registered unit in Unit Registry. Reset its local service
   state, restart the updated service, and register/approve it once using the physical
   code. Approval reuses the original computer-unit GUID and historical records.
   Legacy vaults display RecoveryRequired; they are never silently upgraded or trusted.
4. Future restarts and long holidays require no administrator action. Lost keys,
   revoked credentials, or a deliberately reset/reimaged computer require recovery.

The migration has only been tested against disposable databases, not applied to the
live CLMS database. The development signing secret must be supplied securely in
production. The existing JWT signing configuration supports HS256/384/512; use at
least 32/48/64 random bytes respectively, and never log or commit credentials.

## Configuration and HTTPS

Optional non-secret defaults:

```json
{
  "AgentAuth": { "Audience": "CLMS.Agent", "AccessMinutes": 15 },
  "AgentRateLimits": {
    "AgentDiscovery": 30, "AgentRegistration": 5, "AgentPolling": 30,
    "AgentClaim": 10, "AgentAuthentication": 30, "AgentHeartbeat": 10
  }
}
```

The agent audience must differ from the human audience. Limits use one-minute
windows and an aggregate 120 requests/IP/minute limit. The database additionally
allows at most five outstanding unexpired challenges per device, across server
instances. Proxy deployments must configure trusted forwarded headers explicitly.

All agent and registry API routes require HTTPS, including Development. HTTP is
rejected, not redirected. Use a certificate whose SAN matches the server hostname,
and trust its issuer in each machine's system trust store. A developer's per-user
certificate trust does not establish trust for LocalSystem/root. Never bypass TLS
validation. Keep this service within the intended LAN exposure boundary.

## Registration and proof protocol

The service journals a local key reference before generating a non-exportable key.
Only canonical base64url DER SubjectPublicKeyInfo for ECDSA P-256 is submitted.
Private keys, other curves, RSA, trailing DER bytes, and malformed encodings are rejected.
The public key and SHA-256 fingerprint are immutable for that registration request.
Lab/unit number, hostname, platform, and hardware metadata are not identity proofs.

A pending request contains a server-generated ID, eight-character display code,
private 256-bit registration secret (hashed on the server), and ten-minute expiration.
Expiration and approval use server time. The administrator physically verifies the
code. Approval/rejection are terminal; an approval made before the deadline can be
completed after a later reconnect. Codes and IDs alone cannot complete registration.

Approval creates/reactivates the existing unit, increments its generation, revokes
previous credentials, and associates an inactive public-key credential. Completion
requires the registration secret and a fresh signed completion challenge. The
transaction activates the key and records completion exactly once. Before sending
completion, the service persists the identity identifiers. A lost response is
recovered by private status polling and ordinary authentication, without another
activation or approval. Approved but uncompleted assignments remain reserved until
an administrator deletes the unit. Rejected/expired retries generate new keys.

Challenge responses contain:
`id, nonce, audience, expiresAtUtc, computerUnitId, credentialId, contextId,
replacementCredentialId, replacementFingerprint`.

The signed bytes are UTF-8, in exactly this order, separated by LF (`\n`), including
a final LF. Empty optional fields still occupy a line:

```text
CLMS.Device.v1
purpose
audience
computer-unit-guid
current-credential-guid
context-guid
challenge-guid
nonce
replacement-fingerprint-or-empty
```

GUIDs are lowercase `D` format. Nonces and fingerprints are unpadded canonical
base64url of 32 bytes. The context is the zero GUID for authentication, registration
ID for completion, or client-journaled operation ID for rotation. Purposes are
`authenticate`, `complete`, `rotate-current`, and `rotate-replacement`. Both rotation
proofs bind the current credential and the replacement fingerprint, with different
purposes. The service checks returned identity, audience, and context before signing.

Sign the complete payload using ECDSA/SHA-256. Transmit the 64-byte IEEE P1363 `r || s`
signature as unpadded base64url. The macOS provider converts its native DER signature
to P1363; the server explicitly verifies this format. Public keys use named P-256
OID `1.2.840.10045.3.1.7`, not a private-key container or arbitrary curve parameters.

Challenges have 256-bit random nonces and a 60-second expiration. They are bound to
the device, credential, audience, and purpose. Consumption and credential transitions
use MySQL transactions and the existing laboratory lock, so multiple server instances
cannot consume the same challenge successfully. Invalid signatures consume their
challenge. JWT signing failures roll back the transaction and issue no response token.

JWTs carry `principal_type=agent`, `computer_unit_id`, `device_credential_id`, and
`credential_generation`, with no human roles. Every protected agent request checks
current unit/generation/credential activation and revocation. Delete, disable, or
rotation invalidates existing JWTs immediately through this check, rather than waiting
for expiration. Reactivation cannot restore an old key. Disable retains the assignment;
soft-delete it before enrolling a replacement computer in that assignment.

Registration decisions and credential replacement/revocation history remain in the
database. Challenges record consumption and successful verification timestamps, and
are pruned after 24 hours by an hourly worker, at most 10,000 rows per pass. Server audit logs record successful completion, authentication, and rotation using
unit/credential IDs, and denied attempts using trace IDs only. No logging path includes
tokens, registration secrets, signatures, or private key material.

## Background service behavior and storage

The service authenticates at startup and one minute before JWT expiry. JWTs remain
in memory and are reacquired after every service restart. A semaphore serializes
registration, authentication, rotation, and heartbeat operations. An unauthorized
heartbeat triggers one authentication retry. Network/TLS/timeouts/503/429 preserve
the device identity and use bounded exponential backoff (2–60 seconds with jitter,
or bounded Retry-After). Revoked/unavailable identities enter RecoveryRequired;
key/vault failures display StorageError and retain recovery information.

Windows uses signing-only CNG machine keys in Microsoft Software Key Storage Provider,
with private export forbidden and key ACL restricted to LocalSystem. The service is
an automatic LocalSystem service. DPAPI machine-protects the metadata snapshot in
`%ProgramData%/CLMS/Agent/credentials.dpapi`; the directory permits SYSTEM and
administrators. Windows monitoring retains its local checkpoint and event-log output.

macOS uses permanent, sensitive, non-exportable P-256 keys in the System Keychain,
through native Security.framework APIs. The root daemon uses an access list trusting
its signed executable, independent of an interactive login or MAUI SecureStorage.
Keep a stable Developer ID designated requirement across signed service upgrades.
Key storage/signing fails closed if the native provider or protection is unavailable.
The service snapshot is also in System Keychain; filesystem state is under
`/Library/Application Support/CLMS/Agent`.

The MAUI UI never owns keys or JWTs. Windows named-pipe clients are checked through
identification tokens, after reverting impersonation; the UI verifies the service
using read-only SCM identity/configuration checks. macOS uses the root-owned staff-group
Unix socket; the UI verifies its root peer. Ordinary UI commands expose only registration
and connection status, never credentials. Rotation additionally requires an enabled
Windows administrator SID (elevation) or root UID on macOS.

## Install, reset, and rotate

On Windows with the .NET 10 MAUI workload:

```powershell
dotnet publish CLMS.Agent.Service -c Release -r win-x64 --self-contained true -o artifacts/agent-service
dotnet publish CLMS.Agent -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -o artifacts/agent-ui
```

Run `CLMS.Agent.Service/Deployment/Install-Agent.ps1` elevated with
`-ServicePublishPath artifacts/agent-service -UiPublishPath artifacts/agent-ui`.
Installed binaries do not automatically follow IDE output. The installer preserves
identity state. Closing MAUI or logging out does not stop the service.

For local reset, first revoke/soft-delete the server unit, stop the service, run its
installed executable with `--reset` elevated, then restart the service. Reset removes
local identity references and queues retired-key deletion for the service identity
on its next startup. Local reset alone cannot revoke server credentials. An uninstall
without another startup can leave protected retired keys; revoke the server identity
before retiring or reimaging a machine.

For normal rotation, leave the service running and invoke the installed executable
with `--rotate-key` in an elevated, waiting process. For example, in elevated PowerShell:

```powershell
$process = Start-Process -FilePath "$env:ProgramFiles\CLMS\Service\CLMS.Agent.Service.exe" -ArgumentList '--rotate-key' -WindowStyle Hidden -Wait -PassThru
$process.ExitCode
```

Rotation needs proof from both keys, never just the JWT. The service journals both
keys before the exchange. After a lost response or restart it tries the replacement
first, then the current identity. The old key is deleted only after the replacement
identity is durably confirmed. Transient failures retain this journal for retry.

On macOS publish the self-contained service for `osx-arm64` or `osx-x64` and MAUI for
`net10.0-maccatalyst` with the corresponding runtime. Sign the service apphost, nested
runtime binaries, and `.app` using your stable Developer ID, then notarize distribution.
Run `sudo sh CLMS.Agent.Service/Deployment/install-macos.sh SERVICE_PUBLISH_DIR CLMS.Agent.app`.
The UI is the existing enterprise unsandboxed companion; root launchd owns the service.
Rotate using `sudo /Library/CLMS/Service/CLMS.Agent.Service --rotate-key`. For reset,
boot out the daemon, run `--reset` as root, then bootstrap it again.

The installer does not configure Windows Audit Logon. macOS access monitoring is
provided by the root daemon and the existing Aqua companion, as described below.

## macOS access monitoring

The daemon records `Startup` using native `kern.boottime` and deduplicates by
`kern.bootsessionuuid`. Boot detection works before anyone logs in. A service restart
or clock adjustment does not create another startup for the same boot UUID.

The existing `--watch-ui` Aqua LaunchAgent also observes the primary graphical
session, independently of MAUI. It records the first observed `Sign-in` once per
boot/audit-session ID. Restarting the companion or switching back to an existing
session does not count as a new sign-in. A new login creates a new audit session.

Unlock tracking is **best effort**, using the undocumented distributed notifications
`com.apple.screenIsLocked` and `com.apple.screenIsUnlocked`. It requires an observed
foreground lock followed by unlock. Initial unlocked state, repeated notifications,
sleep/wake alone, and a companion restart do not manufacture unlock events. When a
session switches out, its prior lock hint is discarded. These notifications may be
missed or spoofed, and their names/behavior can change after macOS updates. They are
operational observations, never device-authentication or authorization evidence.
Apple discusses this limitation in its [screen-lock API guidance](https://developer.apple.com/forums/thread/850327).

Core Foundation notifications are pumped on the companion's main thread. UI prompting
and IPC delivery run independently; closing MAUI leaves the companion active. SSH,
unauthenticated loginwindow, root, and non-graphical reporting sessions are rejected.
The service verifies the socket UID and audit token (`LOCAL_PEERTOKEN`) against
read-only audit-session metadata and the primary console. This reads session metadata;
it does not turn on BSM auditing, modify audit policy, require Endpoint Security
entitlements, or require Full Disk Access. An unavailable native check fails closed
for observation delivery without stopping device authentication or heartbeats.

Observed sign-in/unlock timestamps are observation times, not reconstructed historical
login times. They appear in `/var/log/clms-agent.log` with source
`SessionCompanion/Observed/BestEffort`. Startup uses source `KernelBootTime`.
No usernames or user IDs are included in emitted records. The root-owned checkpoint
is `/Library/Application Support/CLMS/Agent/mac-access-checkpoint.json`, separate
from authentication state and the Windows checkpoint.

The user's private directory is `~/Library/Application Support/CLMS/Agent` (mode 0700).
It contains `session-events.json` (0600), the companion process lock, and bounded
`session-monitor.log` diagnostics (with one `.previous` file, 256 KiB each). The
installer sets umask 077 for both launchd jobs. Diagnostics contain fixed health
messages, not usernames, event payloads, or credentials.

The outbox holds at most 256 observations for the current boot/session and drops the
oldest on overflow with a diagnostic. It retains event IDs across network/service
failures. Delivery retries with bounded backoff and drains at most one observation
per second. The service acknowledges after saving its checkpoint, remembers 1,024
recent event IDs plus up to 4,096 sign-in sessions per boot, and limits reporting to
60 requests per minute. Reports from a switched-out session remain queued until that
session is primary again. Previous-boot/session or invalid reports are discarded;
unverified peers, rate limits, and temporary storage failures are retried.

Like Windows event logging, a crash between emitting a log entry and checkpointing
can repeat an entry. Long-delayed retries beyond the bounded ID history can also
repeat an unlock observation. Events while the companion is stopped cannot be
recovered from system history, and reinstalling with an already active login records
only the time that login was first observed by this implementation.

After installing/updating, log out and log back in to start the updated Aqua companion;
boot out and bootstrap the root daemon through the normal installer. The installed
service is not updated by building in Rider. No new production helper executable,
server API, database migration, or MAUI permission is required for monitoring.

Run portable monitoring checks on either development OS:

```powershell
dotnet test CLMS.Tests --no-restore --filter FullyQualifiedName~MacAccess
```

On a Mac, run the native ABI/socket checks in a GUI terminal (the peer test needs the
root identity used by the daemon):

```sh
sudo env CLMS_MAC_MONITOR_NATIVE=1 dotnet test CLMS.Tests --filter FullyQualifiedName~MacAccessNativeTests
```

Before release, build the macOS service/MAUI targets on a Mac and verify reboot,
service restart, companion restart, login/logout, fast user switching, lock/unlock,
sleep/wake without locking, service downtime/reconnect, MAUI closure, and unauthorized
IPC rejection. Confirm the two source labels and absence of personal identities in
logs. Native notification and launchd behavior cannot be validated on Windows.

## API reference

All routes use JSON; registry errors use Problem Details with a stable `code`.
Authentication failures use the same nonsensitive response for unknown, inactive,
revoked, or invalid identities/proofs. Successful challenge issuance does not return
laboratory, hostname, or registration status.

| Method | Route | Request / authorization |
|---|---|---|
| GET | `/api/agent/laboratories` | Anonymous discovery |
| POST | `/api/agent/registrations` | `{laboratoryId,unitNumber,hostname,operatingSystem,agentVersion,publicKey}`; 201 receipt |
| POST | `/api/agent/registrations/{id}/status` | `{secret}`; approved identity and completion state |
| POST | `/api/agent/registrations/{id}/completion-challenge` | `{secret}` |
| POST | `/api/agent/registrations/{id}/claim` | `{secret,challengeId,signature}`; identity acknowledgement, no JWT |
| POST | `/api/agent/auth/challenge` | `{deviceId,credentialId}` |
| POST | `/api/agent/auth/token` | `{deviceId,credentialId,challengeId,signature}`; JWT |
| POST | `/api/agent/auth/rotation-challenge` | Agent JWT; `{operationId,publicKey}` |
| POST | `/api/agent/auth/rotate` | `{deviceId,credentialId,challengeId,signature,replacementSignature}` |
| POST | `/api/agent/heartbeat` | Agent JWT; server timestamps and observes IP |
| GET | `/api/admin/units` | Admin cookie; laboratoryId/search/page/pageSize |
| GET | `/api/admin/unit-registrations` | Admin cookie; status/page/pageSize |
| POST | `/api/admin/unit-registrations/{id}/approve` | Admin cookie + antiforgery; `{physicalCodeVerified:true}` |
| POST | `/api/admin/unit-registrations/{id}/reject` | Admin cookie + antiforgery |
| DELETE | `/api/admin/units/{id}` | Admin cookie + antiforgery |
| POST | `/api/admin/units/{id}/disable` | Admin cookie + antiforgery |

Token responses contain `accessToken`, `accessExpiresAtUtc`, `computerUnitId`,
`generation`, and `credentialId`. No refresh endpoint or refresh expiry is present.

## Verification

```powershell
dotnet test CLMS.Tests --no-restore
$env:CLMS_REGISTRY_INTEGRATION = '1'
dotnet test CLMS.Tests --no-restore --filter FullyQualifiedName~UnitRegistryIntegrationTests
```

Database tests create/drop only generated `clms_registry_test_*` databases. They use
`CLMS_TEST_MYSQL` when supplied, otherwise the development connection after opt-in.
They exercise the actual current schema and forward migration. Use a dedicated
server/account in CI. The default suite skips database cases without opt-in.

An additional native xUnit check is enabled with `CLMS_NATIVE_DEVICE_KEYS=1` under
the service identity (LocalSystem/root); it is skipped during ordinary desktop runs.

The native key harness is `tests/CLMS.DeviceKeys.Checks`. Publish it for the target
OS, sign it on macOS, and run under LocalSystem on Windows or root on macOS. Supply
one randomly generated 32-hex-digit test GUID, never a production key reference:

```text
CLMS.DeviceKeys.Checks create TEST_GUID_N
CLMS.DeviceKeys.Checks verify TEST_GUID_N
CLMS.DeviceKeys.Checks delete TEST_GUID_N
```

Run create and verify in separate processes; optionally reboot between them. Check
that the public fingerprint is unchanged. The harness signs/verifies through the
native provider and checks Windows private export denial; macOS opens fail unless
the key is marked non-exportable. It never reads service state or contacts the server.
Also verify that a normal UI user cannot use the test key, then delete it as its owner.

Release checks on actual Windows/macOS installations must include startup before login,
continued operation after logout/UI close, system certificate trust, key access after
a signed upgrade, denied unauthorized key/IPC access, and revoke/re-register. Windows
cannot validate macOS Keychain, Xcode builds, signing, notarization, or launchd behavior.