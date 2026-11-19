# Workstation sessions

Apply `Database/migrations/2026-11-19-workstation-sessions.sql` to the existing CLMS schema before deploying the web app. It adds `workstation_sessions`. Existing lab attendance and utilization records are separate.

## Lockscreen integration

The lockscreen UI is a separate workstream. Use HTTPS for the following JSON API. In this sequence, the human and agent tokens use their existing separate authorization schemes.

1. Finish the normal CLMS human sign-in flow, including email verification and password change when required. Generate a new UUID for the session and reuse it for retries.
2. With the **human bearer token**, send `POST /api/agent/workstation-sessions/grant` with `{"unitId":"<enrolled-unit-id>","sessionId":"<session-id>"}`. The response contains `grant`, a two-minute proof bound to that user, unit, and session.
3. With the **agent bearer token**, send `PUT /api/agent/workstation-sessions/{sessionId}` with `{"grant":"<grant>"}`. Unlock only after HTTP 204. If a grant expires before the request completes, obtain a fresh grant with the same session ID. Keep the session ID across retries and lockscreen restarts. Do not retain the grant or user token after use.
4. On explicit CLMS sign-out, send `PUT /api/agent/workstation-sessions/{sessionId}/end` with the agent bearer token and no body. Queue an interrupted end for retry with the same ID.

A repeat start of the same active session and a repeat end of the same completed session return 204. A second active session on the same workstation returns 409. A start replay after that session ended returns 409 with `workstation_session_ended`. An end for an unknown session or another workstation returns 404.

Agent heartbeat loss does not close a session. Unit Monitoring keeps connectivity and last seen separate from the active CLMS user. The admin history is at `/unit-monitoring/sessions`. Windows and macOS sign-in events do not feed this history.
