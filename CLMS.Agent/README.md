# CLMS desktop agent

The agent can also receive the faculty's screen through the separate [Faculty Live Stream](../docs/faculty-live-stream.md) feature. Receiving or closing that viewer does not end tracking or monitoring sessions.

One .NET 10 MAUI application runs enrollment, persistent device authentication, the server connection, optional screen capture, and local access monitoring for the signed-in OS user on Windows or Mac Catalyst.

Start the app, configure the HTTPS server, select a laboratory/unit number, and request registration. An administrator verifies the displayed physical code and approves it in Unit Registry. The protected signing key survives application restarts and long offline periods. Raw device keys and refresh tokens are not entered or stored.

The unit details panel shows machine-wide network download and upload rates in bytes per second.
The agent averages byte-counter changes across active adapters every 15 seconds, excluding loopback and tunnel interfaces.
New or reset adapters need two samples before contributing. Virtual adapters may count traffic also seen on a physical adapter.
Readings are kept in server memory and hidden when the unit is offline or the last sample is over 45 seconds old; no database migration is needed.


Settings can be hidden using the tray/menu bar. Capture begins only when an authorized web viewer requests it. Local Stop sharing leaves the connection running. Pause stops network work; Quit/logout stops the application. Login startup is optional and per-user. Access observations depend on OS permissions and application lifetime.

Administrators can open a unit's details in Monitoring and use Send message, Lock workstation, Restart, or Shut down while its agent is connected. Restart and Shut down ask for confirmation. Update and restart both the web app and workstation agents to enable the commands; no database migration is required.

Messages use the same native desktop notifications as other workstation alerts, titled Administrator message. Enable desktop notifications in the agent and allow them in OS settings. Confirmation means the operating system accepted the notification, not that the user saw or read it; the web controls report disabled notifications separately. Power commands request the operating system action without forcing applications closed; unsaved work or OS permissions can prevent completion. A missing confirmation is not proof that the command failed, so check the workstation before retrying. On macOS, allow Automation for System Events and Accessibility for the lock shortcut; native Mac actions require verification on a Mac.

See [architecture and component map](../docs/agent-architecture.md) and [registration/deployment](../docs/unit-registry.md). The old Service/Core/Console hosts and IPC are retired. Existing machine-service installations must be stopped/uninstalled and manually re-enrolled; building this app does not modify an installed service or production database.

```powershell
dotnet build CLMS.Agent/CLMS.Agent.csproj --artifacts-path artifacts/agent-build
```

Build Mac Catalyst on macOS with its MAUI workload; capture requires macOS 15.2 or newer. Windows capture requires Windows 10 version 2004 or newer.

See [notifications and quick controls](../docs/notifications-and-quick-controls.md) for native notification opt-in, bulk workstation controls, and the server integration API for other features.
