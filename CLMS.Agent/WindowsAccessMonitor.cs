using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;

namespace CLMS.Agent;

[SupportedOSPlatform("windows")]
internal sealed class WindowsAccessMonitor(string statePath, Action<UnitAccessEvent> emit)
{
    private Checkpoint state = new();

    /// <summary>Restores the checkpoint and emits startup only when the machine or recorded Windows boot has changed.</summary>
    public void Initialize()
    {
        if (File.Exists(statePath))
        {
            // Fail visibly rather than silently replaying records after a damaged checkpoint.
            state = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(statePath))
                ?? throw new InvalidOperationException("The checkpoint is empty. Use a new --state path to replay this boot.");
        }
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=12]]") { ReverseDirection = true });
        using var boot = reader.ReadEvent();
        if (boot?.TimeCreated is null)
            throw new InvalidOperationException("Windows startup event is unavailable in the System log.");
        var bootUtc = new DateTimeOffset(boot.TimeCreated.Value).ToUniversalTime();
        if (state.Machine != Environment.MachineName || state.BootUtc != bootUtc)
        {
            state = new Checkpoint { Machine = Environment.MachineName, BootUtc = bootUtc };
            emit(new UnitAccessEvent("Startup", bootUtc));
            Save();
        }
    }

    /// <summary>Reads up to 256 qualifying sign-in or unlock events after the checkpoint and advances it after each processed record.</summary>
    public int ReadAvailable()
    {
        using (var latestReader = new EventLogReader(new EventLogQuery("Security", PathType.LogName) { ReverseDirection = true }))
        using (var latest = latestReader.ReadEvent())
        {
            if ((latest?.RecordId ?? 0) < state.SecurityRecordId)
            {
                Console.Error.WriteLine("Security log was cleared; resetting the access-event checkpoint.");
                state.SecurityRecordId = 0;
                Save();
            }
        }
        var since = state.BootUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var query = $"*[System[EventID=4624 and EventRecordID > {state.SecurityRecordId} and TimeCreated[@SystemTime >= '{since}']] and EventData[Data[@Name='LogonType']='2' or Data[@Name='LogonType']='7' or Data[@Name='LogonType']='10' or Data[@Name='LogonType']='11' or Data[@Name='LogonType']='12' or Data[@Name='LogonType']='13']]";
        using var reader = new EventLogReader(new EventLogQuery("Security", PathType.LogName, query));
        var count = 0;
        while (count < 256)
        {
            using var record = reader.ReadEvent();
            if (record is null) break;
            var access = UnitAccessEvent.FromSecurityXml(record.ToXml());
            if (access is not null) emit(access);
            state.SecurityRecordId = record.RecordId ?? state.SecurityRecordId;
            Save();
            count++;
        }
        return count;
    }

    /// <summary>Replaces the checkpoint file through a temporary file so later launches can resume from the recorded event cursor.</summary>
    private void Save()
    {
        File.WriteAllText(statePath + ".tmp", JsonSerializer.Serialize(state));
        File.Move(statePath + ".tmp", statePath, true);
    }

    private sealed class Checkpoint
    {
        public string Machine { get; set; } = "";
        public DateTimeOffset BootUtc { get; set; }
        public long SecurityRecordId { get; set; }
    }
}
