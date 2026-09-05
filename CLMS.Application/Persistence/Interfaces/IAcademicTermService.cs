using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface IAcademicTermService
{
    Task<IEnumerable<AcademicTermDTO>> GetAllAsync();
}