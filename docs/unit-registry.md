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

The current agent is one per-user MAUI application; the previous machine service and IPC deployment are retired. Read [the integrated architecture](agent-architecture.md) for actor locations, storage, monitoring, and capture lifetimes.

Existing registry installations still need `2026-11-08-persistent-device-authentication.sql` if that schema upgrade has not already been applied. This architecture consolidation adds no migration. Do not apply an existing migration twice or modify a live database merely to test the rewrite. Preserve the production signing configuration.

There is one existing registered device. Manually revoke/soft-delete its old assignment in Unit Registry, stop and uninstall the installed service and companion/startup jobs, and re-enroll using the new desktop application. Approval reuses the computer-unit GUID/history through the existing registry recovery flow. No old machine key is exported or imported.

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
and trust its issuer in the enrolled OS user's trust store. The desktop app runs as that user; the retired LocalSystem/root service must not keep running alongside it. Never bypass TLS
validation. Keep this service within the intended LAN exposure boundary.

## Registration and proof protocol

The agent journals a local key reference before generating a non-exportable key.
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
completion, the agent persists the identity identifiers. A lost response is
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
purposes. The agent checks returned identity, audience, and context before signing.

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

## Desktop runtime, storage, and deployment

The MAUI runtime polls enrollment and authenticates with a persistent private key. JWTs remain in memory, renew one minute before expiry, and are reacquired after restart. Registration, proof acquisition, rotation, reset, and heartbeat operations share the coordinator gate. Network/TLS/429/503 failures preserve identity and use bounded retry/backoff. Revocation requires recovery; lost or inaccessible keys never cause silent re-enrollment.

Windows uses non-exportable current-user CNG P-256 keys and a DPAPI CurrentUser metadata vault under `%LOCALAPPDATA%/CLMS/Agent`. Mac uses non-extractable keys and metadata in the user's default login Keychain with signed-app access control. Keep a stable application signing identity across Mac upgrades. Settings observes a credential-free status object; token acquisition is internal to the transport/coordinator. The owning OS user can invoke rotation or reset. Rotation keeps the existing dual-proof and durable journal protocol; reset does not revoke server credentials.

On Windows:

```powershell
dotnet publish CLMS.Agent/CLMS.Agent.csproj -c Release -r win-x64 --self-contained true -o artifacts/agent-desktop
```

Install the resulting application in a stable directory and use its login-startup control. On macOS publish the MAUI project for `net10.0-maccatalyst` with the corresponding architecture, sign the app with the stable Developer ID, and notarize distribution. Install it in Applications before enabling login startup. Capture requires macOS 15.2+ and Screen Recording permission. There is no service installer or root launchd job in this deployment.

Rotation and reset are available in settings. Before resetting or retiring a device, revoke its server credential as an administrator. Local reset clears the cached JWT and journals retirement/deletion of its keys. After a lost rotation response, the coordinator probes the replacement first and only deletes the old key after durable confirmation.

Access monitoring now runs in-process. Windows needs event-log read permissions and successful Audit Logon; unavailable monitoring is shown without breaking authentication/capture. Mac observes kernel boot metadata, graphical session sign-in, and best-effort foreground lock/unlock notifications on the MAUI main run loop. Checkpoints/outbox and bounded `access-events.jsonl` are private to the OS user. No usernames or authentication credentials are logged. Hiding settings leaves monitoring active; logout/Quit ends it. Logged-out/pre-login coverage and reconstruction of missed Mac events are not provided.

Run shared tests and browser checks described in [agent-architecture.md](agent-architecture.md). Native Keychain, capture, startup, signing, and Mac ABI tests require a real Mac. Database integration tests require a disposable configured MySQL database. Building in Rider does not update installed binaries or stop an old installed machine service.

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