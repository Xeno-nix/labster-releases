using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface ICourseRepository
{
    Task<int> CreateAsync(Course course);
    Task<bool> UpdateAsync(Course course);
    Task<bool> DeleteAsync(int id);
    Task<IEnumerable<Course>> GetAllAsync();

    Task<Course?> GetByIdAsync(int id);

}
