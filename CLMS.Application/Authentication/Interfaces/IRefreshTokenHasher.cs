namespace CLMS.Application.Authentication.Interfaces;

public interface IRefreshTokenHasher
{
    string Hash(string refreshToken);

    bool Verify(string refreshToken, string hashToken);
}
