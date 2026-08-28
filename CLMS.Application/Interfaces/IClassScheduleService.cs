using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface IClassScheduleService
{
    Task<IEnumerable<ClassScheduleDto>> GetAllAsync();

    Task<ClassScheduleDto?> GetByIdAsync(int id);

    Task<int> CreateAsync(UpsertClassScheduleDTO dto);

    Task UpdateAsync(int id, UpsertClassScheduleDTO dto);

    Task DeleteAsync(int id);
}