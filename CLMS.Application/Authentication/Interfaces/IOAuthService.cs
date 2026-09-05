using CLMS.Shared.Contracts;

namespace CLMS.Application.Authentication.Interfaces;
public interface IOAuthService
{
    Task<ExternalUserInfo> AuthenticateAsync(string authorizationCode);
}
