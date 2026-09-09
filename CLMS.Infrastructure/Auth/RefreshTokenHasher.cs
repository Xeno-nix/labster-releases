using CLMS.Application.Authentication.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace CLMS.Infrastructure.Auth;

public sealed class RefreshTokenHasher : IRefreshTokenHasher
{
    public string Hash(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        byte[] tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        byte[] hash = SHA256.HashData(tokenBytes);

        return Convert.ToBase64String(hash);
    }

    public bool Verify(string refreshToken, string hash)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        ArgumentNullException.ThrowIfNull(hash);

        byte[] tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        byte[] actualHash = SHA256.HashData(tokenBytes);

        byte[] expectedHash;

        try
        {
            expectedHash = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHash);
    }
}