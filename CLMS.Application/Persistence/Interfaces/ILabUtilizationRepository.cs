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

    /// <summary>
    /// Dashboard aggregates over attendance_logs: total sessions with no time-out yet,
    /// total sessions logged today (CURDATE), and active-session counts per laboratory
    /// (only labs with at least one active session are listed). When <paramref name="facultyId"/>
    /// is set, only sessions where that user is the faculty in charge are counted.
    /// </summary>
    Task<(int ActiveNow, int SessionsToday, List<(int LaboratoryId, int Active)> ByLab)>
        GetDashboardStatsAsync(Guid? facultyId);
}
