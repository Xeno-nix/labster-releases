using CLMS.Domain.Entities.Identity;

namespace CLMS.Application.Persistence.Interfaces;

public interface IResetTokenRepository
{
    Task<ResetToken?> FindByUserIdAsync(string userId);
    Task<bool> CreateAsync(ResetToken resetToken);
}