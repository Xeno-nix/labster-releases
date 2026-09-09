using CLMS.Domain.ValueObjects;
namespace CLMS.Domain.Entities.Authentication;
public class Guest
{      
    public Guid GuestId { get; set; }
    public Name FirstName { get; set; }
    public Name? MiddleName { get; set; }
    public string? MiddleInitial => MiddleName?.Value.Substring(0, 1).ToUpper();
    public Name LastName { get; set; }
    public string Organization { get; set; }
    public string ContactNumber { get; set; }
    public string Purpose { get; set; }

    public Guest (Guid guestId, Name firstName, Name middleName, Name lastName, string organization, string contactNumber, string purpose)
    {
        GuestId = guestId;
        FirstName = firstName;
        MiddleName = middleName;
        LastName = lastName;
        Organization = organization;
        ContactNumber = contactNumber;
        Purpose = purpose;
    }

    public string GetFullName()
    {
        string fullname = FirstName.Value;
        if (string.IsNullOrWhiteSpace(MiddleInitial))
        {
            fullname += $" {MiddleInitial}";
        }

        if(string.IsNullOrWhiteSpace(LastName.Value))
        {
            fullname = $" {LastName.Value}";
        }

        return fullname;
    }
}
