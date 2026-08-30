using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface IClassScheduleRepository
{
    Task<IEnumerable<ClassScheduleDTO>> GetAllAsync();

    Task<ClassScheduleDTO?> GetByIdAsync(int id);

    Task<int> CreateAsync(ClassSchedule schedule);

    Task UpdateAsync(ClassSchedule schedule);

    Task DeleteAsync(int id);
}