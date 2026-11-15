using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Identity.Interfaces;

/// <summary>Cryptographic operations are implemented in Infrastructure; no credential hash is exposed to clients.</summary>
public interface IAccountSecurityCrypto : IResetTokenHasher
{
    string GenerateToken();
    string GenerateCode();
    string Stamp(User user);
    bool StampMatches(User user, string stamp);
}
