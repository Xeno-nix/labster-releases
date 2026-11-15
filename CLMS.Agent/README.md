# CLMS desktop agent

The agent runs in the signed-in OS user's session and connects to CLMS.Web automatically after setup. It has a small settings/status window and a Windows tray or Mac menu-bar indicator. Screen viewing happens in the web dashboard; there is no local preview.

## Build and run

For Ethernet/LAN testing, the server laptop can use the supplied PowerShell helpers.
Run setup as the same OS user who runs CLMS.Web, using that laptop's current Ethernet IPv4 address:

```powershell
powershell -NoProfile -File scripts/Setup-LanServer.ps1 -ServerIp 169.254.207.105
```

This creates and trusts a local server certificate, keeping its private key in the current user's certificate store.
It exports only the public certificate to `artifacts/lan/CLMS-LAN.cer`.
In a separate PowerShell opened as Administrator, run `scripts/Enable-LanFirewall.ps1` to allow TCP 7190 on the configured local address from the local subnet.
Then, as the normal server user, start the LAN server:

```powershell
powershell -NoProfile -File scripts/Start-LanServer.ps1
```

Stop any previous CLMS.Web process using port 7190 first.
The launcher serves both the configured Ethernet address and localhost with the LAN certificate; it does not change existing User Secrets.
Copy `CLMS-LAN.cer` to each Windows test laptop and install it in **Current User > Trusted Root Certification Authorities**, then restart the agent.
Use the printed HTTPS server address and each unit's own device key in the agent.
For a hub without DHCP, automatically assigned 169.254.x.x addresses need subnet mask 255.255.0.0 on both laptops.
If the server IP changes, repeat setup with the new IP, reinstall its new public certificate on clients, and enable the firewall rule for the new address.

Windows, from the solution root:

```powershell
dotnet build CLMS.Agent/CLMS.Agent.csproj -t:Run
```

The project targets Windows on Windows and Mac Catalyst on Mac. Install the .NET 10 SDK and corresponding MAUI workload on each build host. On Mac, use the compatible Xcode/.NET Apple workloads and MAUI 10.0.110 or later:

```sh
dotnet build CLMS.Agent/CLMS.Agent.csproj -t:Run
```

Mac capture requires Mac Catalyst 18.2 (macOS 15.2) or later. A Windows build does not compile or validate the Mac implementation.

## Manually provision a device

Until the unit registry is ready, provision a distinct random key for each unit. The web server stores its SHA-256 hash; the agent stores the original key in OS secure storage.

Use an existing computer_units.computer_unit_id. A temporary GUID is supported for a connection/live-view test, but it does not create a database unit.

Generate the original key and hash in PowerShell:

```powershell
$keyBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyBytes)
$rng.Dispose()
$deviceKey = [Convert]::ToBase64String($keyBytes)
$sha = [Security.Cryptography.SHA256]::Create()
$keyHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($deviceKey))).Replace('-', '')
$sha.Dispose()
```

Replace the GUID placeholder below with an actual GUID. Use device indexes 1, 2, etc. for additional PCs; do not reuse a key or overwrite another PC's index.

```powershell
dotnet user-secrets set "AgentConnections:Devices:0:ComputerUnitId" "<your-computer-unit-guid>" --project CLMS.Web
dotnet user-secrets set "AgentConnections:Devices:0:KeySha256" "$keyHash" --project CLMS.Web
dotnet run --project CLMS.Web --launch-profile https
```

Display $deviceKey locally and enter that original value in the agent's password field. Never enter the hash, commit raw keys, or include them in logs.

## One-time setup and normal operation

1. Launch the agent under the OS user who will run it.
2. Enter the HTTPS web-server address and original device key.
3. Click **Save setup and connect**. The address goes into MAUI preferences; the key goes into MAUI SecureStorage. The key field clears and never displays the saved key.
4. Check the acknowledged unit GUID and timestamp.
5. Enable **Start when this OS user signs in** if desired.
6. Click **Hide settings**. Reopen it using the tray/menu-bar indicator.

On later launches, the agent loads setup and connects automatically. By default it hides its settings after loading valid saved input, even if the server is temporarily offline. Turn off **Open in the background after setup** to keep settings visible on launch. Missing or unreadable setup keeps settings visible.

**Reconnect** reloads saved setup; save edited values first. Entering a new key and saving replaces the old key. A blank key field reuses the saved key.

**Pause connection** cancels retries, stops sharing and disconnects until Reconnect or the next app launch. **Stop screen sharing** stops the current viewing session; an authorized viewer can request a new session later. **Quit agent** stops the runtime and removes the indicator. Quitting does not disable future sign-in startup.

There is no OS-administrator authentication gate on this settings window; device provisioning is performed by the person administering the PC. Settings and credentials are per OS user, not shared automatically across student accounts.

## Connection and screen behavior

- The authenticated SignalR endpoint is /hubs/agents; heartbeats are acknowledged every 15 seconds.
- Each connection attempt has a 20-second timeout.
- SignalR first retries a lost connection immediately, then after 2, 10 and 30 seconds.
- The runtime checks every 15 seconds and starts another connection after initial failure or exhausted SignalR retries.
- Saving new setup cancels the old runtime before using the replacement credentials.
- Capture starts only when an authorized web viewer requests it. Disconnects, reconnects, pause, quit and lease expiry stop the old session; a viewer must start a fresh session.
- Frames are JPEGs, at most two per second and 512 KiB each. Only the latest frame is retained in memory; no recording is made.
- HTTPS certificate validation remains enabled. Local development uses https://localhost:7190. Other PCs need a reachable server hostname, appropriate listening/firewall configuration, and a trusted certificate matching that hostname. localhost always means the agent's own PC.

This connection does not create database units, update unit_availability or write screen-cast database logs. The monitoring UI overlays authenticated live agent presence on database unit records, updating cards every five seconds. The configured ComputerUnitId must match the actual PC's computer_units.computer_unit_id. Disconnects show Offline; heartbeats older than two minutes also show Offline. Live presence is held in this web-server process and is rebuilt when agents reconnect after a restart. Legacy units without live presence retain their database status.

## Platform behavior and installation

### Windows

Windows capture uses a persistent Windows Graphics Capture compositor session (Windows 10 version 2004 or later).
It no longer calls GDI screen-copy operations on every frame, avoiding that capture path's cursor-blinking behavior.
The native session remains open during viewing and is released on stop, error, disconnect or quit.
On newer supported Windows versions, the agent requests Windows' borderless-capture permission once per process and disables the border when permission is granted. Windows may show a consent prompt. Denial, policy restrictions or an older OS leave the normal capture border enabled; sharing still works. The package manifest declares graphicsCaptureWithoutBorder for packaged builds. Windows may also retain a border if another capturing app requires one. The cursor is excluded from the transmitted image without hiding the physical cursor.
Retest cursor stability, display-resolution changes, stop and restart on the actual monitored PC after updating.

When testing a new build, choose **Quit agent** from the tray or settings before replacing files or launching the update.
Closing the settings window only hides the old running agent; a second launch opens that same process.
For a deployed PC, republish and replace the whole published folder after quitting the old agent. A browser refresh alone cannot update native capture code.

Closing the settings window hides it and leaves the agent connected. Left-click the tray icon to open settings; right-click for Open settings, Stop screen sharing and Quit agent. A second app launch brings up the existing agent instead of starting a duplicate connection. Explorer restarts recreate the tray icon.

This project is currently **unpackaged**. The startup switch writes only this user's CLMS.Agent value in HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run, pointing to the current executable. It does not install a Windows service. Windows Startup Apps settings can override the entry.

For a stable deployment, publish into a permanent folder and enable startup from that copy:

```powershell
dotnet publish CLMS.Agent/CLMS.Agent.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/agent-windows
```

Distribute the entire publish folder, not just the EXE. Choose the runtime matching the target PCs. Debug startup entries point to the build folder; disable/re-enable startup after moving the app. Disable startup before deleting an installed copy.

### Mac Catalyst

The **CLMS** menu-bar indicator provides settings, stop sharing, hide and quit. It changes to **CLMS • Sharing** during capture. Hide keeps the agent running. The settings window's close control is disabled and the Close menu command removed so the single Catalyst scene stays alive; use Hide or Quit.

The native menu-bar integration uses a small Objective-C bridge for AppKit status items, which Catalyst does not expose directly. If indicator creation fails, settings stay visible. A process activity keeps the connection from being throttled by App Nap while still allowing the Mac to sleep.

Install a signed build in a permanent Applications location before enabling login startup. The switch uses SMAppService.MainApp. If approval is required, allow it in System Settings > General > Login Items, then reopen settings to refresh the displayed status. Enable startup separately for each OS user. Code signing/distribution must be configured on the Mac.

The first web capture may request screen-recording permission. If denied, enable CLMS.Agent under System Settings > Privacy & Security > Screen Recording (or Screen & System Audio Recording), then restart the agent. The application sandbox/network entitlements do not grant this permission.

## Manual acceptance checks

1. **First setup:** start without saved setup. Settings stay visible and show setup required. Save a valid address and key; verify the assigned unit GUID and heartbeat timestamp.
2. **Persistence:** quit using the agent's menu and relaunch. It connects without key entry and, by default, only the indicator remains. Reopen settings and confirm the key is not displayed.
3. **Web viewing:** sign in to CLMS.Web as Admin or Faculty, open https://localhost:7190/unit-monitoring, wait up to five seconds for the matching PC card to show Available or In Use, then select it. Its preview starts automatically. Move a window on the agent PC and verify live updates. Admin users can open View all screens for automatic previews of up to sixteen PCs on the current page. Screen-wall tiles show only the screen image, with no names, status text or buttons. Use the main Unit Monitoring page for details and viewing controls. Changing the page or laboratory disposes previews that leave the view. Closing the details on the main monitoring page, hiding the browser tab, clicking Stop viewing or using the agent's Stop sharing ends the session. A local Stop sharing action is not automatically retried while the unit stays online; use Start viewing or reopen the preview to request a new session.
4. **Hidden window:** hide settings (or close it on Windows). The web view and heartbeats continue.
5. **Stop sharing:** use the indicator menu. The browser clears its image. Start a new viewing session and verify it works.
6. **Server outage:** stop the web server, then restart it. Verify the agent reconnects automatically without opening settings. Start a fresh viewing session; the old session must not resume.
7. **Server unavailable at launch:** start the agent before the web server. Start the server later and verify automatic connection.
8. **Pause/resume:** pause from settings, wait at least 30 seconds, and verify it remains disconnected. Reconnect and verify recovery.
9. **Change key:** save a wrong key and verify rejection. Save the provisioned key again and verify recovery. Replace the server-side hash and matching agent key to test rotation.
10. **Quit:** quit during viewing. The stream ends and the tray/menu-bar indicator disappears.
11. **Sign-in startup:** enable startup, sign out and back into the same OS user, and verify background connection. Disable startup, repeat sign-out/sign-in and verify it does not launch. No connection is expected before user sign-in.
12. **Windows duplicate launch:** open the executable again while it is running. Settings should open and the web should still show a single unit connection.
13. **Mac:** test permissions denied/granted, hide/reopen, close-control behavior, login-item approval, sleep/wake, and both supported CPU architectures on actual hardware.

Per-lab viewer restrictions are not implemented yet; live viewing requires Admin/Faculty authorization and session ownership. The details modal views one unit at a time. The screen wall automatically requests previews for the current page and displays screen images only. Automatic starts happen only during interactive rendering, not during server prerendering.

## Automated verification

```powershell
dotnet build CLMS.Agent/CLMS.Agent.csproj --no-restore
dotnet test CLMS.Tests/CLMS.Tests.csproj --no-restore
node --test CLMS.Tests/ScreenWallLayout.test.mjs
node --test CLMS.Tests/UnitScreenView.test.mjs
```

If a running web process locks the normal build output, keep it running and use a separate output folder:

```powershell
dotnet test CLMS.Tests/CLMS.Tests.csproj --no-restore -p:OutDir=C:/path/to/clms/artifacts/background-tests/ -p:UseAppHost=false
```

Runtime tests exercise missing/unreadable setup, startup retry, recovery after a closed connection, cancellation, replacement credentials, prevention of overlapping connections and disposal. Hub tests use a real authenticated in-memory TestServer, ephemeral data-protection keys and no Windows Event Log provider. They do not need MySQL or staff credentials. The database-backup integration test requires its separately configured environment.

Automated tests do not validate actual OS secure-storage persistence, tray/menu interaction, screen permission dialogs or sign-in startup. Perform the acceptance checks above. Mac code must be built and exercised on a Mac.

## Files and responsibilities

| File | Responsibility |
| --- | --- |
| App.xaml.cs | Creates settings, starts the runtime, updates the indicator, and coordinates shutdown. |
| MauiProgram.cs | Registers shared services and selects native Windows/Mac implementations. |
| Services/AgentSetupStore.cs | Validates setup; stores the address in preferences and key in secure storage. |
| Services/IAgentSetupStore.cs | Read-only setup contract used by the runtime and tests. |
| Services/AgentRuntimeService.cs | Owns automatic connection, retries, restart, pause and cancellation independently of the page. |
| Services/IAgentConnectionTransport.cs | Small transport contract so runtime behavior can be tested without MAUI. |
| Services/AgentConnectionService.cs | Authenticated SignalR transport, acknowledgements, heartbeats, reconnect and capture commands. |
| Services/ScreenCastSender.cs | Captures and sends frames only during an authorized viewing session. |
| Services/IDesktopAgentService.cs | Contract for native window visibility, indicator and login startup. |
| Views/MainPage.xaml and .xaml.cs | Setup/status controls, startup switches, pause/reconnect, stop-sharing, hide and quit. |
| Platforms/Windows/WindowsDesktopAgentService.cs | Win32 tray/menu, close-to-hide behavior, Explorer recovery and per-user Run registration. |
| Platforms/Windows/App.xaml.cs | Prevents duplicate agent processes and signals the existing settings window. |
| Platforms/MacCatalyst/MacDesktopAgentService.cs | Mac status menu, hide/show, login-item registration and process activity. |
| Platforms/MacCatalyst/AppDelegate.cs | Removes Close from the Catalyst menu to preserve the settings scene. |
| Platforms/Windows/WindowsScreenCaptureService.cs | Persistent compositor capture, JPEG encoding, resize handling and native cleanup behind IScreenCaptureService. |
| Platforms/Windows/WindowsGraphicsCaptureInterop.cs | Native primary-monitor and Direct3D device interop for Windows Graphics Capture. |
| Platforms/MacCatalyst/MacScreenCaptureService.cs | ScreenCaptureKit/JPEG capture behind the same interface. |
| ../CLMS.Tests/AgentRuntimeTests.cs | Tests the runtime using controlled setup and transport fakes. |
| ../CLMS.Tests/AgentConnectionTests.cs | Authenticated hub, live-view, lease, stop and frame-delivery integration checks. |

AppShell remains from the MAUI template; App now opens the injected settings page directly.
