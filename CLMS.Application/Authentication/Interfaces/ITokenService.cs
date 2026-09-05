using CLMS.Domain.Entities.Authentication;

namespace CLMS.Application.Authentication.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(User user);
        string GenerateRefreshToken();
    }
}
