using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(UserClaimsDto user);
        string GenerateRefreshToken();
        TimeSpan RefreshTokenLifetime { get; }
    }
}
