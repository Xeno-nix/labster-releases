using CLMS.Domain.Enums;

namespace CLMS.Domain.Entities.Identity;

public sealed record RolePrivilege(Role Role, string Privilege);
public sealed record UserPrivilegeOverride(Guid UserId, string Privilege, bool IsGranted);
