using CLMS.Domain.Entities.UtilizationLogging;

namespace CLMS.Domain.Repositories;

public interface ILabUtilizationRepository
{
    Task AddAsync(LaboratoryUseLog log, CancellationToken cancellationToken = default);
    Task UpdateAsync(LaboratoryUseLog log, CancellationToken cancellationToken = default);

    /// <summary>The student's currently open (no time-out) session, if any.</summary>
    Task<LaboratoryUseLog?> GetActiveSessionForStudentAsync(string studentId, CancellationToken cancellationToken = default);

    /// <summary>All currently open sessions logged under a guest with this exact name.</summary>
    Task<List<LaboratoryUseLog>> GetActiveSessionsForGuestNameAsync(string guestName, CancellationToken cancellationToken = default);
}
