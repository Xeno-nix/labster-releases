# Labster desktop app

One .NET 10 MAUI application runs enrollment, persistent device authentication, the server connection, and optional screen capture for the signed-in OS user on Windows or Mac Catalyst.

Start the app and sign in with the email and password of an existing Labster Admin or Super Admin account. The login form uses the workstation's saved HTTPS server automatically. For a fresh installation, deployment can supply `CLMS_AGENT_SERVER_URL`; local development defaults to `https://localhost:7190`. An existing enrollment always uses its saved server. Complete the email code if enabled; initial passwords must first be changed in the web app. Faculty, Student, and Unassigned accounts cannot open settings. The server reads the current account role, and the desktop rechecks authorization before settings operations and every 30 seconds. Unavailable authorization locks settings.

Human credentials and access tokens are never persisted by the desktop. Settings lock when hidden, when an unlocked window loses focus, or after ten minutes. A pending email challenge survives switching apps to read the code. The unused human refresh credential is revoked after login. Existing workstation authentication and background operation continue while settings are locked; opening the tray/menu-bar settings requires another sign-in. Tray Quit also requires unlocked settings. Stop screen sharing remains available locally without sign-in.

After sign-in, select a laboratory/unit number and request registration. An administrator verifies the displayed physical code and approves it in Unit Registry. The protected signing key survives application restarts and long offline periods. Raw device keys and refresh tokens are not entered or stored. Deploy the updated web server together with this agent: the new `/api/agent/settings/access` endpoint is required. The agent uses the web logo and shared maroon, gold, surface, text, and error palette with native controls on Windows and Mac Catalyst.

Settings can be hidden using the tray/menu bar. Capture begins only when an authorized web viewer requests it. Local Stop sharing leaves the connection running. Pause stops network work; Quit/logout stops the application. Login startup is optional and per-user. See [workstation sessions](../docs/workstation-sessions.md) for the separate CLMS lockscreen contract.

See [architecture and component map](../docs/agent-architecture.md) and [registration/deployment](../docs/unit-registry.md). The old Service/Core/Console hosts and IPC are retired. Existing machine-service installations must be stopped/uninstalled and manually re-enrolled; building this app does not modify an installed service or production database.

```powershell
dotnet build CLMS.Agent/CLMS.Agent.csproj --artifacts-path artifacts/agent-build
```

Build Mac Catalyst on macOS with its MAUI workload; capture requires macOS 15.2 or newer. Windows capture requires Windows 10 version 2004 or newer.
