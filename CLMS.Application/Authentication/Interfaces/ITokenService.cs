using CLMS.Domain.Entities.Authentication;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Authentication.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(UserClaimsDto user);
        string GenerateRefreshToken();
        TimeSpan RefreshTokenLifetime { get; }
    }
}
