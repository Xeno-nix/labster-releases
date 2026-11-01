using System.Data.Common;
using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface IClassScheduleRepository
{
    Task<IEnumerable<ClassScheduleDTO>> GetAllAsync();

    Task<ClassScheduleDTO?> GetByIdAsync(int id);

    /// <summary>
    /// Non-deleted schedules taught by one instructor in one term, each with StudentCount
    /// populated. Unlike GetAllAsync this does not filter on the instructor's role_type.
    /// </summary>
    Task<IEnumerable<ClassScheduleDTO>> GetByInstructorAsync(Guid instructorId, int termId);

    /// <summary>
    /// Every non-deleted schedule in a term, each with StudentCount populated.
    /// Used by the Admin class list view.
    /// </summary>
    Task<IEnumerable<ClassScheduleDTO>> GetAllForTermAsync(int termId);

    /// <summary>
    /// All schedule ids belonging to the same "class" as <paramref name="scheduleId"/>:
    /// same course, instructor, term, year level and section. Always includes the given id.
    /// NULL year level or section yields a group of one.
    /// </summary>
    Task<IReadOnlyList<int>> GetGroupScheduleIdsAsync(int scheduleId);

    /// <summary>
    /// Same grouping as <see cref="GetGroupScheduleIdsAsync"/> but returning the full rows
    /// (day/time, StudentCount, ...), ordered Monday-first. Used by the sync status.
    /// </summary>
    Task<IReadOnlyList<ClassScheduleDTO>> GetGroupSchedulesAsync(int scheduleId);

    /// <summary>
    /// Finds the class schedule (course/lab/instructor resolved) a student is enrolled
    /// in that is currently in session for the given day/time. Added for the logging
    /// terminal's Student Time-In flow — see ClassScheduleRepository for the query.
    /// </summary>
    Task<ClassScheduleDTO?> FindCurrentForStudentAsync(string studentId, DayOfWeek dayOfWeek, TimeOnly currentTime);

    Task<int> CreateAsync(ClassSchedule schedule);

    Task UpdateAsync(ClassSchedule schedule);

    Task DeleteAsync(int id);

    /// <summary>
    /// Soft-deletes every given schedule that is still active (sets deleted_at), one statement
    /// per batch, on the caller's connection/transaction. Used by delete class list to remove a
    /// whole class — all of its meeting slots — together. Returns the number of rows affected.
    /// </summary>
    Task<int> DeleteManyAsync(
        IEnumerable<int> scheduleIds,
        DbConnection connection,
        DbTransaction transaction);
}