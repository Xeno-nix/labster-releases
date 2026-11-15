# Lab screen-cast sessions

Open `/unit-monitoring/screens`, select a laboratory, then select **Start session**.
The existing screen-wall page remains restricted to Admin users. The service also
accepts Faculty identities for authorized callers. Select **End session** before
switching laboratories. Each PC needs its configured CLMS agent connected to the
LAN server and permission to capture the screen.

## Existing database tables

The feature uses the existing `screen_cast_sessions` and
`screen_cast_connections` tables in the application's configured database.
No table creation or migration is required.

One `screen_cast_sessions` row records the initiating staff user, UTC times,
`active` / `ended` / `failed` status, and a random server-generated session token.
The token stays on the server; browser ownership is checked against the signed-in
user. Per-PC frame IDs remain separate temporary UUIDs used by the agent protocol.

The session snapshots all registered PCs belonging to the selected laboratory.
Each gets one `screen_cast_connections` row, initially `disconnected`. A row
becomes `connected` when capture is requested successfully for its preview.
Paging away, local Stop sharing, disconnect, or capture expiry changes it back to
`disconnected`. Reopening that preview reuses the row and records its latest
connection interval. These tables store session/connection metadata, not video
recordings or every reconnection interval.

## Live behavior

The agent stays connected while the PC is on. Connecting the agent or opening
PC details does not start screen capture. In the lab wall, capture starts after
**Start session**; in individual PC details, it waits for **Start viewing**.
Ending the viewing session stops capture while keeping the agent connected.

The wall uses its existing pagination, with at most 16 live previews per viewer.
Capture runs for the current page; other PCs remain listed in the session but
disconnected until viewed. Offline PCs can start when their agents connect and
the wall refreshes. A failed or locally stopped preview has a **Retry preview**
button. New PCs registered after a session starts require a new lab session.
Click a PC's screen preview to open an enlarged live view. Close it with **Close**
or **Escape** to return to the wall. The enlarged view shares the existing frame
stream and does not start another capture session.

Only the initiating user can view, renew, or end their lab session. The server
rejects PCs outside the selected lab and rechecks membership before capture.
One active session per laboratory is allowed on this server. Individual PC
previews outside the lab wall continue using the existing temporary service.

The browser renews the lab lease every five seconds, including when all PCs are
offline. Hiding or leaving the page ends the session when cleanup reaches the
server. Without cleanup or browser requests, its 15-second lease expires and
the server marks it `failed` and stops its captures. Normal End session records
`ended`. Disconnect and expiry history is reconciled every second. Database
failures do not prevent capture from stopping; pending history updates retry.

This is a single-server implementation with in-memory live state. On the first
new lab session after a process restart, persisted `active` sessions from the
previous process become `failed` and their connections become `disconnected`.
Use one web-server process for this deployment.

## Verification

```powershell
dotnet test CLMS.Tests/CLMS.Tests.csproj --artifacts-path artifacts/screen-cast-tests
node CLMS.Tests/LabScreenCastLease.test.mjs
node CLMS.Tests/ScreenWallLayout.test.mjs
node CLMS.Tests/UnitScreenView.test.mjs
```

The separate build directory avoids overwriting DLLs held by a running LAN
server. Restart/redeploy that server to load the updated code.
Automated service tests use an in-memory history repository; live MySQL
and capture from multiple physical PCs require deployment verification.
