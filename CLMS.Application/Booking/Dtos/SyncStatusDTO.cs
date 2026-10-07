namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// Sync state of one class group, computed from live data on every page load (never stored).
/// A class is in sync when every meeting slot carries the same roster as the group union —
/// the union is the sync target, so "missing" is always relative to it.
/// </summary>
public class SyncStatusDTO
{
    public bool IsInSync { get; set; }

    public int TotalSlots { get; set; }

    /// <summary>Students on the class roster (the union across slots — what every slot should have).</summary>
    public int RosterSize { get; set; }

    public IReadOnlyList<SyncSlotDTO> Slots { get; set; } = [];
}

/// <summary>One meeting slot's contribution to the sync status.</summary>
public class SyncSlotDTO
{
    public int ScheduleId { get; set; }

    /// <summary>e.g. "Mon 08:00 - 10:00"</summary>
    public string Label { get; set; } = string.Empty;

    public int StudentCount { get; set; }

    /// <summary>Roster students this slot is missing (0 when the slot is in sync).</summary>
    public int MissingCount { get; set; }
}