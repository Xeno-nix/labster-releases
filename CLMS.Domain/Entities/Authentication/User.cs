using CLMS.Domain.Common;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;

namespace CLMS.Domain.Entities.Authentication;

public class User : AuditableEntity
{
    public Name FirstName { get; set; }
    public Name LastName { get; set; }
    public Name? MiddleInitial { get; set; }
    public Name? Suffix { get; set; }
    public string Email { get; set; }
    public Role UserRole { get; set; }
    public string PasswordHash { get; set; }

    public User(Guid id, Name firstName, Name lastName, string email, Role userRole, string passwordHash)
    {
        _id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        UserRole = userRole;
        PasswordHash = passwordHash;
    }

    public User(Guid id, Name firstName, Name lastName, Name? middleInitial, Name? suffix, string email, Role userRole, string passwordHash)
    {
        _id = id;
        FirstName = firstName;
        LastName = lastName;
        MiddleInitial = middleInitial;
        Suffix = suffix;
        Email = email;
        UserRole = userRole;
        PasswordHash = passwordHash;
    }

    public User()
    {
    }
    public string GetFullName()
    {
        string fullName = FirstName.Value;
        if (MiddleInitial != null)
        {
            fullName += $" {MiddleInitial.Value}.";
        }
        fullName += $" {LastName.Value}";
        if (Suffix != null)
        {
            fullName += $" {Suffix.Value}";
        }
        return fullName;
    }

}