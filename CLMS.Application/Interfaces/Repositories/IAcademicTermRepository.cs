using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface IAcademicTermRepository
{
    Task<IEnumerable<AcademicTerm>> GetAllAsync();
}