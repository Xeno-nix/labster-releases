using CLMS.Domain.Common;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;

namespace CLMS.Domain.Entities.Users;

public class User : AuditableEntity
{
    public Name FirstName { get; set; }
    public Name LastName { get; set; }
    public Name? MiddleName { get; set; }
    public Name? Suffix { get; set; }
    public string Email { get; set; }
    public Role UserRole { get; set; }

    public User(Guid id, Name firstName, Name lastName, string email, Role userRole)
    {
        _id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        UserRole = userRole;
    }

    public User(Guid id, Name firstName, Name lastName, Name? middleName, Name? suffix, string email, Role userRole)
    {
        _id = id;
        FirstName = firstName;
        LastName = lastName;
        MiddleName = middleName;
        Suffix = suffix;
        Email = email;
        UserRole = userRole;
    }

    public User()
    {
    }
    public string GetFullName()
    {
        string fullName = FirstName.Value;
        if (MiddleName != null)
        {
            fullName += $" {MiddleName.Value}";
        }
        fullName += $" {LastName.Value}";
        if (Suffix != null)
        {
            fullName += $" {Suffix.Value}";
        }
        return fullName;
    }

}