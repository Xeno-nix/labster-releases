using CLMS.Shared.Contracts;

namespace CLMS.Application.Identity.Interfaces;
public interface IOAuthService
{
    Task<ExternalUserInfo> AuthenticateAsync(string authorizationCode);
}
