using CLMS.Application.Authentication.Interfaces;
using CLMS.Shared.Contracts;

namespace CLMS.Infrastructure.Auth;

public class GoogleOAuth2 : IOAuthService
{
    private readonly string _apikey;

    public GoogleOAuth2(string apikey)
    {
        _apikey = apikey;
    }

    public Task<ExternalUserInfo> AuthenticateAsync(string authorizationCode)
    {
        throw new NotImplementedException();
    }
}