# Console unit-access logging prototype

Run this agent **on the workstation being monitored**, separately from CLMS.Web.
It prints the workstation, laboratory, event, and PH timestamp. No database writes,
server calls, user identities, or separate activity-log page are added.

In an elevated Windows terminal:

```powershell
dotnet run --project CLMS.Agent -- --unit-name LAB1-PC-01 --lab "Computer Laboratory 1"
```

Without `--unit-name`, the Windows computer name is used. Laboratory defaults to
`Not configured`; these are test labels, not registered/verified database identities.
Output appears in the **agent terminal**, not the browser or web-server console.
Stop with Ctrl+C. `--once` processes currently available events and exits.
`--self-test` runs deterministic checks and prints clearly simulated sample events.

## Events

- **Startup**: the latest Windows System `Microsoft-Windows-Kernel-General` event 12,
  using its recorded startup time. This is Windows startup, not the exact electrical
  power-on instant; sleep/resume does not generate a new Startup here.
- **Sign-in**: Security event 4624, interactive logon types 2, 10, 11, or 12.
- **Unlock**: Security event 4624, logon types 7 or 13. Network, service, batch,
  machine, and Windows helper-account logons are excluded.

The agent polls every two seconds. On the first run it reads access events from
the current Windows boot, so an earlier sign-in can appear with its original time.
Later starts resume from the checkpoint rather than treating an agent restart as
a workstation startup. A cleared Security log resets the record cursor.

The checkpoint defaults to `%LOCALAPPDATA%/CLMS/unit-access-checkpoint.json`.
Use `--state "C:/path/checkpoint.json"` to choose a stable writable location.
Run only one instance per checkpoint. Deduplication is per checkpoint, so use the
same account/path between runs. A crash between printing and checkpoint saving
can repeat the last event; this console prototype does not promise exactly-once delivery.

## Windows prerequisites

The agent needs read access to System and Security logs (an elevated terminal is
the simplest test setup). Successful **Audit Logon** events must be enabled in
Windows audit policy for sign-in/unlock events to exist. The agent does not change
audit policy, install a service, or register a startup task.

To capture events immediately at boot rather than replay them after manual launch,
the agent must eventually be deployed as an automatically started agent/service.
For now, leave it running and lock/unlock the workstation to test live output.
Restart it with the same checkpoint to test that already processed records are not repeated.
To replay this boot deliberately, choose a fresh `--state` path.

Windows event references:
[4624 logon types](https://learn.microsoft.com/en-us/windows/security/threat-protection/auditing/event-4624),
[EventLogReader](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.eventing.reader.eventlogreader).
