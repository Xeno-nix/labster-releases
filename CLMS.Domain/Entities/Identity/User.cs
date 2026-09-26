using CLMS.Domain.Common;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;

namespace CLMS.Domain.Entities.Identity;

public class User : AuditableEntity
{
    public Name FirstName { get; set; }
    public Name LastName { get; set; }
    public Name? MiddleName { get; set; }
    public string? MiddleInitial => MiddleName?.Value.Substring(0, 1).ToUpper();
    public Name? Suffix { get; set; }
    public string Email { get; set; }
    public Role UserRole { get; set; }
    public string PasswordHash { get; set; }

    public User(Guid id, Name firstName, Name lastName, string email, Role userRole , string passwordHash, DateTime? deletedAt = null)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        UserRole = userRole;
        DeletedAt = deletedAt;
        PasswordHash = passwordHash;
    }

    public User(Guid id, Name firstName, Name lastName, Name? middleName, Name? suffix, string email, Role userRole, string passwordHash, DateTime? deletedAt = null)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        MiddleName = middleName;
        Suffix = suffix;
        Email = email;
        UserRole = userRole;
        DeletedAt = deletedAt;
        PasswordHash = passwordHash;
    }
    
    public User(Guid id, Name firstName, Name lastName, Name? middleName, Name? suffix, string email, Role userRole, string passwordHash, DateTime createdAt, DateTime modifiedAt, DateTime? deletedAt = null)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        MiddleName = middleName;
        Suffix = suffix;
        Email = email;
        UserRole = userRole;
        DeletedAt = deletedAt;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
        LastModifiedAt = modifiedAt;
    }
    
    public User(Guid id, Name firstName, Name lastName, string email, Role userRole , string passwordHash, DateTime createdAt, DateTime modifiedAt, DateTime? deletedAt = null)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        UserRole = userRole;
        DeletedAt = deletedAt;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
        LastModifiedAt = modifiedAt;
    }

    public User()
    {
    }
    public string GetFullName()
    {
        string fullName = FirstName.Value;
        if (MiddleName != null)
        {
            fullName += $" {MiddleInitial}.";
        }
        fullName += $" {LastName.Value}";
        if (Suffix != null)
        {
            fullName += $" {Suffix.Value}";
        }
        return fullName;
    }

}