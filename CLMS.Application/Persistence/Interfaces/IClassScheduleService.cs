using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface IClassScheduleService
{
    Task<IEnumerable<ClassScheduleDTO>> GetAllAsync();

    Task<ClassScheduleDTO?> GetByIdAsync(int id);

    Task<int> CreateAsync(UpsertClassScheduleDTO dto);

    Task UpdateAsync(int id, UpsertClassScheduleDTO dto);

    Task DeleteAsync(int id);

    Task<bool> CheckScheduleConflictAsync(UpsertClassScheduleDTO dto, int? excludingClassScheduleId = null);
}