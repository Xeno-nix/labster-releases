using System.Data.Common;
using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces.Repositories;

/// <summary>
/// Operations on the student_enrollment table. Everything here is keyed on a schedule id
/// (a single weekly meeting), or a list of ids when the caller operates on a whole class
/// group at once.
/// <para>
/// Methods that participate in a transaction accept an optional <c>DbConnection</c> /
/// <c>DbTransaction</c> (BCL types, so Application stays Infrastructure-free). When omitted
/// the repository opens and owns its own connection — preserving current behaviour for
/// existing callers.
/// </para>
/// <para>
/// student_enrollment has no audit columns: removing a student is a hard DELETE.
/// </para>
/// </summary>
public interface IEnrollmentRepository
{
    /// <summary>
    /// Students enrolled in one schedule slot, ordered by last name then first name.
    /// Filters out soft-deleted schedules. NULL-safe over the four nullable name columns.
    /// </summary>
    Task<IReadOnlyList<EnrolledStudentDTO>> GetRosterAsync(int scheduleId);

    /// <summary>
    /// Student ids enrolled in any of the given schedule slots.
    /// </summary>
    Task<IReadOnlyList<string>> GetEnrolledStudentIdsAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null);

    /// <summary>
    /// Inserts every student into every slot, ignoring rows that already exist
    /// (UNIQUE(student_id, class_schedule_id)). Returns the number of rows newly inserted.
    /// </summary>
    Task<int> EnrollAsync(
        IEnumerable<int> scheduleIds,
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null);

    /// <summary>
    /// Hard-deletes one student from every given slot. Returns rows deleted.
    /// </summary>
    Task<int> RemoveAsync(
        IEnumerable<int> scheduleIds,
        string studentId,
        DbConnection? connection = null,
        DbTransaction? transaction = null);

    /// <summary>
    /// Hard-deletes every enrollment in the given slots (used by delete class list and by
    /// sync when a slot moves into a different group). Returns rows deleted.
    /// </summary>
    Task<int> DeleteAllAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null);

    /// <summary>
    /// Distinct student count per schedule slot, for the sync status. Only counts slots that
    /// still exist (deleted_at IS NULL); a slot with no enrollments is absent from the map.
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> GetCountsAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null);
}