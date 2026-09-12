namespace CLMS.Shared.Contracts;

public record ExternalUserInfo(
    string Provider,
    string ProviderUserId,
    string Email,
    string Name
);