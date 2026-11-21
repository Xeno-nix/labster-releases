namespace CLMS.Domain.Enums;

public enum Role
{
    Admin = 0,
    Faculty = 1,
    SuperAdmin = 2,
    Student = 3, // Legacy database value; not an assignable staff role.
    Unassigned = -1,
}
            

