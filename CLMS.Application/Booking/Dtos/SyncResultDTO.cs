namespace CLMS.Application.DTOs.Booking;

/// <summary>What a sync pass changed.</summary>
public class SyncResultDTO
{
    /// <summary>Slots that had roster students missing before the sync and were filled.</summary>
    public int SlotsUpdated { get; set; }

    /// <summary>The roster size copied into the slots (the union across the group).</summary>
    public int RosterSize { get; set; }

    /// <summary>True when every slot was already up to date and nothing changed.</summary>
    public bool AlreadySynced => SlotsUpdated == 0;
}