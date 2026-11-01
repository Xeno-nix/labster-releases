using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;
public interface IAcademicTermRepository
{
    Task<int> CreateAsync(AcademicTerm term);
    Task<bool> UpdateAsync(AcademicTerm term);
    Task<bool> DeleteAsync(int id);
    Task<IEnumerable<AcademicTerm>> GetAllAsync();

    /// <summary>
    /// The term containing <paramref name="today"/>, falling back to the most recent term
    /// by start date when none covers it. NULL start/end dates are treated as open-ended.
    /// Returns null when academic_terms is empty.
    /// </summary>
    Task<AcademicTerm?> GetCurrentAsync(DateOnly today);
}
