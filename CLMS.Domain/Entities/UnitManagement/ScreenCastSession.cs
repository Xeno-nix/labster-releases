namespace CLMS.Domain.Entities.UnitManagement;

public sealed class ScreenCastSession
{
    public int CastSessionId { get; set; }
    public Guid InitiatedBy { get; set; }
    public string Status { get; set; } = "active";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string SessionToken { get; set; } = "";
}
