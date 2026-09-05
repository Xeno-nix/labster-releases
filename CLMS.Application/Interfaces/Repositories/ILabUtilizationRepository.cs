using CLMS.Domain.Entities.UtilizationLogging;

namespace CLMS.Application.Interfaces.Repositories;

public interface ILabUtilizationRepository
{
    Task<int> CreateAsync(LaboratoryUseLog log);
    Task UpdateAsync(LaboratoryUseLog log);

    /// <summary>The student's currently open (no time-out) session, if any.</summary>
    Task<LaboratoryUseLog?> GetActiveSessionForStudentAsync(string studentId);

    /// <summary>All currently open sessions logged under a guest with this exact name.</summary>
    Task<List<LaboratoryUseLog>> GetActiveSessionsForGuestNameAsync(string guestName);
}
