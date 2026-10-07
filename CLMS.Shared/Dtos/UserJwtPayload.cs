namespace CLMS.Shared.Contracts;

public record UserJwtPayload(string Email, Guid Id, string UserRole);
