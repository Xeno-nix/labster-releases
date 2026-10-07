using System.Data.Common;
using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Entities.Users;

namespace CLMS.Application.Interfaces.Repositories;

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(string studentId);

    /// <summary>
    /// NULL-safe lookup of a student's display info, for pre-filling the add-student modal.
    /// Unlike <see cref="GetByIdAsync"/> this never throws on NULL name parts or on names
    /// containing characters the Name value object rejects.
    /// </summary>
    Task<EnrolledStudentDTO?> FindByIdAsync(string studentId);

    /// <summary>
    /// NULL-safe batch version of <see cref="FindByIdAsync"/> for the CSV import preview.
    /// Returns matches in no particular order; ids with no row are simply absent.
    /// </summary>
    Task<IReadOnlyList<EnrolledStudentDTO>> FindByIdsAsync(
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null);

    /// <summary>
    /// The student ids from the given list that already exist in the students table.
    /// </summary>
    Task<IReadOnlyList<string>> GetExistingIdsAsync(
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null);

    /// <summary>
    /// Inserts the given students, leaving an id that already exists untouched
    /// (ON DUPLICATE KEY UPDATE student_id = student_id) so a concurrent insert by another
    /// request can't fail the batch. Only the five columns that exist in the students table
    /// are written — Email / ContactNumber have no backing columns.
    /// </summary>
    Task CreateManyAsync(
        IEnumerable<Student> students,
        DbConnection? connection = null,
        DbTransaction? transaction = null);
}