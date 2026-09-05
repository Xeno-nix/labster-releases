using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface IClassScheduleRepository
{
    Task<IEnumerable<ClassScheduleDTO>> GetAllAsync();

    Task<ClassScheduleDTO?> GetByIdAsync(int id);

    /// <summary>
    /// Finds the class schedule (course/lab/instructor resolved) a student is enrolled
    /// in that is currently in session for the given day/time. Added for the logging
    /// terminal's Student Time-In flow — see ClassScheduleRepository for the query.
    /// </summary>
    Task<ClassScheduleDTO?> FindCurrentForStudentAsync(string studentId, DayOfWeek dayOfWeek, TimeOnly currentTime);

    Task<int> CreateAsync(ClassSchedule schedule);

    Task UpdateAsync(ClassSchedule schedule);

    Task DeleteAsync(int id);
}