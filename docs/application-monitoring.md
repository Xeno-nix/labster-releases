# Application monitoring

The follow-on [Abnormal Activity Monitoring](abnormal-activity-monitoring.md) module adds review flags and an activity lifecycle to this scan flow. Apply its additional migration when deploying the current server.

Apply `Database/migrations/2026-11-21-application-monitoring.sql` before deploying the server. This follows the existing manual MySQL migration convention. No process snapshots are written to the database.

Administrators manage the registry at `/admin/applications`. Active administrator authorization is checked inside the registry and icon services as well as on the page. Enabled entries contain a display name, an exact process name, a platform, and an optional uploaded icon. Process names are trimmed, case-folded, and stripped of a trailing `.exe`. Use separate Windows/macOS entries where names differ. The database uniquely indexes platform and normalized process name.

Uploads are limited to PNG/WebP, checked against their content type and file header, and capped at 256 KB. Generated filenames live under `CLMS.Web/wwwroot/application-icons`. Keep this directory writable by the server and preserve it across deployments. Back it up with the database; the existing database backup feature does not include static files. The generic icon is `/images/application-default.svg`.

Authorized Admin/Faculty viewers open a unit's details to request a scan, then use the panel's Refresh button. Authorization and non-deleted unit/laboratory membership are checked on the server, following the existing screen monitoring access model (staff currently have access across laboratories). Instructors receive only detected names and icon paths; the full registry is never sent to their browser.

The existing authenticated agent SignalR hub carries registry synchronization, invalidation notices, scan requests, and ID-only scan replies. The agent loads the enabled platform-specific registry after the initial authenticated connection, on reconnect, and after a change notification. Cache replacement is atomic; failed refreshes preserve the previous cache. Failed initial synchronization returns RegistryUnavailable, distinct from a successful empty scan. A missed change notification is recovered on reconnect.

Scans run in the shared agent runtime without any reference to MAUI views. `Process.GetProcesses()` is called only for an explicit scan command. Process objects are disposed, inaccessible/exited processes are skipped, repeated instances are collapsed, and a platform-specific entry takes precedence over a Both entry for the same process. The current repository hosts this runtime in the desktop application; it does not contain a separate Windows Service or macOS LaunchDaemon host.

The single-server coordinator shares an in-flight scan among requesting viewers, enforces a 15-second cooldown per unit, and waits up to eight seconds for a response. During cooldown it returns the latest result rather than requesting another scan. Only the latest snapshot is held in memory. Replies must match the pending request, authenticated unit, and current connection. Names/icons are resolved against the current enabled registry before delivery through the existing Blazor Server circuit. UI countdown updates do not request scans.

Windows and macOS use the same implementation. Exact process names can differ by OS and some system processes may be inaccessible; this is observation only. Native macOS enumeration/build verification requires a Mac. No process termination, policy classification, process history, or periodic process polling is implemented.

Run `dotnet test CLMS.Tests/CLMS.Tests.csproj`. The focused matching/cache/coordinator/authorization tests run without a database or native UI. The authenticated SignalR tests exercise startup, change notification, scans, and reconnection. Set `CLMS_TEST_MYSQL` (or the existing `CLMS_REGISTRY_INTEGRATION=1` opt-in) to run the migration/repository test against a disposable test database.
