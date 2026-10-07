namespace CLMS.Application.Identity.Interfaces;

public interface IRefreshTokenHasher
{
    string Hash(string refreshToken);

    bool Verify(string refreshToken, string hashToken);
}
