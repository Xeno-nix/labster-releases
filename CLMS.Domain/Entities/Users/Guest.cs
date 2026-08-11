using CLMS.Domain.ValueObjects;

namespace CLMS.Domain.Entities.Users;
internal class Guest
{      
    public Guid GuestId { get; set; }
    public string GuestName { get; set; }
    public string Organization { get; set; }
    public string ContactNumber { get; set; }
    public string purpose { get; set; }
}
