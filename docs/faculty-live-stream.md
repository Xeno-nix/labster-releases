# Faculty live stream

Faculty screen broadcasting is available within an active monitoring session on the screen wall. Its owner can start and stop broadcasting to that session's laboratory. Ending or expiring the monitoring session stops its broadcast. No database migration is required.

## Use

1. Run the updated web server and the updated CLMS.Agent on each workstation.
2. Sign in as Faculty or Admin and open **Unit Monitoring → View all screens**. Select the laboratory and click **Start monitoring session**. This enables **Share screen**.
3. Sharing uses the laboratory selected in the monitor toolbar. Confirm the displayed laboratory and click **Choose what to share**. Use the browser picker to choose a screen, window, or tab. When sharing starts, the dialog closes and the toolbar shows the live connection count. The laboratory selector stays locked while sharing.
4. Connected, authorized workstations assigned to that lab automatically open a receive-only viewer in full-screen mode. **Exit full screen** switches to a normal window; **Close viewer** dismisses the stream. Closing or stopping the viewer restores the agent window's previous presentation mode. Workstations connecting later join while the broadcast remains active.
5. Click **Stop sharing** to end the broadcast and continue monitoring, or **End monitoring session** to end both. Keep the page open when presenting another application.

Workstation users can close their viewer without interrupting tracking or monitoring. The same broadcast will not reopen a viewer they closed; starting a new broadcast permits joining again. Only one broadcast may run per laboratory, and a faculty account can run one broadcast at a time. Only its initiating account can operate it.

The first version shares video only. It does not request workstation camera/microphone access, share audio, record the presentation, or grant remote control. Share an application window if the presenting computer is also registered in the target lab, to avoid showing a recursive desktop preview.

## Delivery and limits

The faculty browser uses `getDisplayMedia` and a send-only WebRTC connection per workstation. The agent's local WebView receives video. SDP offers/answers travel over the existing authenticated server connection; enrollment credentials stay in the native agent and are never passed to the WebView. SDP is bounded and is not logged or stored in the database.

This initial deployment uses direct LAN WebRTC host candidates, with no external STUN/TURN service. Faculty and workstation peers must be able to reach each other; HTTPS access to the web server alone does not establish a media route. Network client isolation, blocked peer traffic, or separate NATs can prevent video. Routed/remote deployments need a separately configured WebRTC relay/media-server design. Each workstation adds an outbound video connection on the faculty machine, so validate the intended class size on the actual network.

Server ownership, registry authorization, lab membership, and connection identity are checked during signaling. The browser renews a 90-second broadcast lease. Lost browser/server connections stop media locally and expire server state; agents close their viewer on transport disconnect. A revoked, moved, or disconnected workstation is removed on the next browser poll. Stale offers/answers cannot join a replacement stream.

The agent waits for the local viewer script's readiness marker before starting WebRTC; an initial blank WebView navigation is not considered ready. The native bridge starts negotiation through a synchronous wrapper and polls for its answer, avoiding platform-specific JavaScript Promise return handling. Result decoding accepts raw JSON, JSON-encoded strings, and MAUI's quote-trimmed but still escaped JavaScript string results, preserving SDP line endings and escaped characters. Setup/signaling failures keep the viewer open with a safe diagnostic code and Close viewer button. The presenter must start a new broadcast to retry a failed or declined connection. Full-screen transitions and application visibility do not themselves dismiss the viewer; closing uses the viewer controls or native Back action.

If a workstation viewer opens and immediately closes, update its agent to include this startup handling. If the updated viewer shows `page_load_timeout`, `unsupported_webrtc`, `script_execution_failed`, or `viewer_start_failed`, check its WebView runtime. `offer_rejected`, `answer_failed`, or `invalid_answer` indicate negotiation failures; update both sides. `bridge_response_invalid` indicates an unreadable native bridge result; update the agent. `ice_timeout` or `answer_timeout` indicate setup timing problems. `signaling_failed` indicates that the server could not confirm the connection. A viewer that stays on Connecting after answering can still indicate blocked direct LAN media traffic. These diagnostics do not include SDP, credentials, or native exception messages. Verify actual playback on the intended laptops after deploying the updated agent.

## Validation

Run `dotnet test CLMS.Tests/CLMS.Tests.csproj --filter "FullyQualifiedName~FacultyStream|FullyQualifiedName~FacultyViewerHandshake|FullyQualifiedName~AgentConnectionTests"` and `node --test CLMS.Tests/FacultyLiveStream.test.mjs`.

Build the Windows agent on Windows and the Mac Catalyst agent on macOS. Release validation needs an actual faculty browser and enrolled workstation on the intended LAN: verify video movement, late arrival, viewer close, presenter stop, browser/server loss, and simultaneous monitoring. Windows builds cannot verify Mac WebView playback. Browser capture still requires the faculty's screen selection.
