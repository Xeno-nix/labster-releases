using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface ICourseService
{
    Task<IEnumerable<CourseDTO>> GetAllAsync();

    Task<CourseDTO?> GetByIdAsync(int id);

}