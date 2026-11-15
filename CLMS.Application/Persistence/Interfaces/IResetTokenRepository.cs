using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Persistence.Interfaces;

public interface IResetTokenRepository
{
    Task<ResetToken?> FindByUserIdAsync(string userId);
    Task<bool> CreateAsync(ResetToken resetToken);
    /// <summary>Checks the original password hash, consumes an unexpired proof, and revokes refresh sessions atomically.</summary>
    Task<bool> ConsumeAsync(Guid userId, string expectedTokenHash, string expectedPasswordHash, string newPasswordHash,
        CancellationToken cancellationToken = default);
}
