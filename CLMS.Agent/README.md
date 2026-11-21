# Labster desktop app

One .NET 10 MAUI application runs enrollment, persistent device authentication, the server connection, and optional screen capture for the signed-in OS user on Windows or Mac Catalyst.

Start the app, configure the HTTPS server, select a laboratory/unit number, and request registration. An administrator verifies the displayed physical code and approves it in Unit Registry. The protected signing key survives application restarts and long offline periods. Raw device keys and refresh tokens are not entered or stored.

Settings can be hidden using the tray/menu bar. Capture begins only when an authorized web viewer requests it. Local Stop sharing leaves the connection running. Pause stops network work; Quit/logout stops the application. Login startup is optional and per-user. See [workstation sessions](../docs/workstation-sessions.md) for the separate CLMS lockscreen contract.

See [architecture and component map](../docs/agent-architecture.md) and [registration/deployment](../docs/unit-registry.md). The old Service/Core/Console hosts and IPC are retired. Existing machine-service installations must be stopped/uninstalled and manually re-enrolled; building this app does not modify an installed service or production database.

```powershell
dotnet build CLMS.Agent/CLMS.Agent.csproj --artifacts-path artifacts/agent-build
```

Build Mac Catalyst on macOS with its MAUI workload; capture requires macOS 15.2 or newer. Windows capture requires Windows 10 version 2004 or newer.
