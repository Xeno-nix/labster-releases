namespace CLMS.Domain.Enums;

public static class RoleExtensions
{
    public static string ToLowerCaseString(this Role role)
    {
        return role.ToString().ToLower();
    }
}