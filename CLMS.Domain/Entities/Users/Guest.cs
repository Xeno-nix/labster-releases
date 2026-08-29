namespace CLMS.Domain.Entities.Users;

/// <summary>
/// Mirrors the Laravel `guest_logs` table. Each visitor time-in creates one row here
/// (a group reservation produces one Guest per attendee, all sharing the same booking).
/// </summary>
public class Guest
{
    public int Id { get; private set; }
    public string GuestName { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? ContactNumber { get; set; }
    public string? Purpose { get; set; }

    public Guest() { } // EF Core / repository materialization

    public Guest(string guestName, string? purpose = null)
    {
        if (string.IsNullOrWhiteSpace(guestName))
            throw new ArgumentException("guestName is required", nameof(guestName));

        GuestName = guestName;
        Purpose = purpose;
    }

    /// <summary>
    /// Assigns the identity once persisted. Intended for repository implementations
    /// (EF Core sets this via materialization; the in-memory repo calls it directly
    /// after generating an id).
    /// </summary>
    public void AssignId(int id) => Id = id;
}
