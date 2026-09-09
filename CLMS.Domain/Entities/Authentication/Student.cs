using CLMS.Domain.Common;
using CLMS.Domain.ValueObjects;
namespace CLMS.Domain.Entities.Authentication;

public class Student : AuditableEntity
{
    public Name FirstName { get; set; }
    public Name LastName { get; set; }
    public Name? MiddleName { get; set; }
    public string? MiddleInitial => MiddleName?.Value.Substring(0, 1).ToUpper();
    public Name? Suffix { get; set; }
    
    public Student(Guid id, Name firstName, Name lastName, Name? middleInitial, Name? suffix)
    {
        _id = id;
        FirstName = firstName;
        LastName = lastName;
        MiddleName = middleInitial;
        Suffix = suffix;
    }

}

