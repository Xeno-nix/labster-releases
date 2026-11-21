using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using System.Data.Common;
using System.Text.RegularExpressions;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Common;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;

namespace CLMS.Application.Services;

/// <summary>
/// Class list management: grouping a course's weekly meetings into one "class", and
/// managing the students on that class's roster.
/// <para>
/// Authorization lives here, not only in the UI: every method that takes a schedule id
/// re-checks that the caller is an Admin or the class's own instructor. That closes the case
/// where a faculty user calls the service with another instructor's schedule id.
/// </para>
/// </summary>
public class ClassListService
{
    private readonly ICurrentUser actor;
    private readonly IClassScheduleRepository _scheduleRepository;
    private readonly IAcademicTermRepository _termRepository;
    private readonly IEnrollmentRepository _enrollmentRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly ClassListCsvParser _csvParser;

    public ClassListService(
        IClassScheduleRepository scheduleRepository,
        IAcademicTermRepository termRepository,
        IEnrollmentRepository enrollmentRepository,
        IStudentRepository studentRepository,
        IDbConnectionFactory dbFactory,
        ClassListCsvParser csvParser, ICurrentUser actor)
    {
        this.actor = actor;
        _scheduleRepository = scheduleRepository;
        _termRepository = termRepository;
        _enrollmentRepository = enrollmentRepository;
        _studentRepository = studentRepository;
        _dbFactory = dbFactory;
        _csvParser = csvParser;
    }

    /// <summary>
    /// The classes visible to the current user in a term. Administrators see every class,
    /// faculty see only what they teach. Returns one entry per class, not per meeting.
    /// </summary>
    /// <param name="termId">Null resolves the current term, falling back to the latest.</param>
    public async Task<IReadOnlyList<ClassListClassDTO>> GetClassesAsync(int? termId = null)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsView);
        var current = await actor.GetActorAsync();
        var userId = current.Id;
        var role = current.UserRole;
        var term = termId.HasValue
            ? (await _termRepository.GetAllAsync()).FirstOrDefault(t => t.Id == termId.Value)
            : await _termRepository.GetCurrentAsync(DateOnly.FromDateTime(DateTime.Now));

        if (term is null)
            return [];

        var schedules = Privileges.HasAdministrativeScope(role)
            ? await _scheduleRepository.GetAllForTermAsync(term.Id)
            : await _scheduleRepository.GetByInstructorAsync(userId, term.Id);

        return GroupIntoClasses(schedules);
    }

    /// <summary>
    /// Collapses schedule rows into classes. Rows share a class when course, instructor,
    /// term, year level and section all match. A null year level or section means the row
    /// has no siblings to merge with, so it becomes a class of one.
    /// </summary>
    private static List<ClassListClassDTO> GroupIntoClasses(
        IEnumerable<ClassScheduleDTO> schedules)
    {
        return schedules
            .GroupBy(s => new
            {
                s.CourseId,
                s.InstructorId,
                s.AcademicTermId,
                s.YearLevel,
                // Normalize whitespace so " A" and "A" land in the same class.
                Section = s.Section?.Trim().ToUpperInvariant()
            })
            .Select(group =>
            {
                // Ordering by the enum value gives Monday-first, matching how a week reads.
                var slots = group
                    .OrderBy(s => s.DayOfWeek)
                    .ThenBy(s => s.TimeStart)
                    .Select(s => new ClassSlotDTO
                    {
                        ScheduleId = s.Id,
                        DayOfWeek = s.DayOfWeek,
                        TimeStart = s.TimeStart,
                        TimeEnd = s.TimeEnd,
                        LaboratoryId = s.LaboratoryId,
                        LaboratoryName = s.LaboratoryName,
                        StudentCount = s.StudentCount
                    })
                    .ToList();

                // Any row works as the representative; take the earliest meeting so the
                // canonical slot is stable across page loads.
                var first = group
                    .OrderBy(s => s.DayOfWeek)
                    .ThenBy(s => s.TimeStart)
                    .First();

                return new ClassListClassDTO
                {
                    RepresentativeScheduleId = first.Id,
                    CourseId = first.CourseId,
                    CourseCode = first.CourseCode,
                    CourseName = first.CourseName,
                    InstructorId = first.InstructorId,
                    InstructorName = first.InstructorName,
                    AcademicTermId = first.AcademicTermId,
                    AcademicYear = first.AcademicYear,
                    Semester = first.Semester,
                    YearLevel = first.YearLevel,
                    Section = first.Section?.Trim().ToUpperInvariant(),
                    Slots = slots,

                    // Roster size comes from the representative slot. Slots that disagree
                    // are reported separately by the sync status, not here.
                    StudentCount = first.StudentCount
                };
            })
            .OrderBy(c => c.YearLevel ?? int.MaxValue)
            .ThenBy(c => c.Section ?? string.Empty)
            .ThenBy(c => c.CourseCode)
            .ToList();
    }

    /// <summary>
    /// Confirms the caller may act on this class: admins may act on any class, faculty only
    /// on classes they teach. Throws otherwise.
    /// </summary>
    private async Task<ClassScheduleDTO> EnsureCanManageAsync(int scheduleId)
    {
        var current = await actor.GetActorAsync();
        var userId = current.Id;
        var role = current.UserRole;
        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId);

        if (schedule is null)
            throw new DomainException("Class schedule not found.");

        if (!Privileges.HasAdministrativeScope(role) && schedule.InstructorId != userId)
            throw new DomainException("Not allowed.");

        return schedule;
    }

    /// <summary>
    /// The roster (students + names) of one class, verified against the caller's rights.
    /// Reads only the representative slot — every slot in the group carries the same roster
    /// once a sync has run.
    /// </summary>
    public async Task<IReadOnlyList<EnrolledStudentDTO>> GetRosterAsync(int scheduleId)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsView);
        await EnsureCanManageAsync(scheduleId);

        return await _enrollmentRepository.GetRosterAsync(scheduleId);
    }

    /// <summary>
    /// NULL-safe name lookup for the add-student modal. No authorization here: it exposes
    /// only a student id and name parts.
    /// </summary>
    public async Task<EnrolledStudentDTO?> FindStudentAsync(string studentId)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsView);
        var normalized = studentId.Trim();

        if (!Regex.IsMatch(normalized, ClassListCsvParser.StudentIdPattern))
            throw new DomainException($"{studentId} is not a valid student ID. Expected format YYYY-NNNNN (4 or 5 digits).");

        return await _studentRepository.FindByIdAsync(normalized);
    }

    /// <summary>
    /// Enrolls a student in every meeting of the class in one transaction. When the student
    /// id is new to the students table, the student row is created first (same transaction).
    /// Returns a human-readable result message; already-enrolled ids are skipped, so enrolling
    /// twice reports "already enrolled" instead of failing.
    /// </summary>
    public async Task<string> AddStudentAsync(int scheduleId,
        AddStudentDTO input)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsManage);
        await EnsureCanManageAsync(scheduleId);

        var normalizedId = input.StudentId.Trim();

        if (!Regex.IsMatch(normalizedId, ClassListCsvParser.StudentIdPattern))
            throw new DomainException($"{input.StudentId} is not a valid student ID. Expected format YYYY-NNNNN (4 or 5 digits).");

        var groupIds = await _scheduleRepository.GetGroupScheduleIdsAsync(scheduleId);

        await using var connection = _dbFactory.Create();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var existing = await _studentRepository.GetExistingIdsAsync([normalizedId], connection, transaction);

            string displayName;

            if (existing.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName))
                    throw new DomainException($"Student {normalizedId} is new — first and last name are required.");

                var student = new Student(
                    normalizedId,
                    new Name(input.FirstName.Trim()),
                    new Name(input.LastName.Trim()));

                if (!string.IsNullOrWhiteSpace(input.MiddleName))
                    student.SetMiddleName(new Name(input.MiddleName.Trim()));

                if (!string.IsNullOrWhiteSpace(input.Suffix))
                    student.SetSuffix(input.Suffix.Trim());

                await _studentRepository.CreateManyAsync([student], connection, transaction);

                displayName = BuildDisplayName(
                    input.FirstName.Trim(),
                    input.LastName.Trim(),
                    input.MiddleName,
                    input.Suffix);
            }
            else
            {
                displayName = (await _studentRepository.FindByIdAsync(normalizedId))?.DisplayName
                    ?? normalizedId;
            }

            var added = await _enrollmentRepository.EnrollAsync(groupIds, [normalizedId], connection, transaction);

            await transaction.CommitAsync();

            return added > 0
                ? $"Enrolled {displayName} ({normalizedId})."
                : $"{displayName} ({normalizedId}) is already enrolled in this class.";
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Removes a student from every meeting of the class in one transaction. Returns the
    /// number of roster rows deleted (0 when the student wasn't enrolled).
    /// </summary>
    public async Task<int> RemoveStudentAsync(int scheduleId,
        string studentId)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsManage);
        await EnsureCanManageAsync(scheduleId);

        var groupIds = await _scheduleRepository.GetGroupScheduleIdsAsync(scheduleId);

        await using var connection = _dbFactory.Create();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var removed = await _enrollmentRepository.RemoveAsync(groupIds, studentId, connection, transaction);

            await transaction.CommitAsync();

            return removed;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Builds the sync status of a class from live data: whether every meeting slot carries
    /// the same roster as the group union (the sync target), and per-slot missing counts.
    /// Never stored — recomputed on every call.
    /// </summary>
    public async Task<SyncStatusDTO> GetSyncStatusAsync(int scheduleId)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsView);
        await EnsureCanManageAsync(scheduleId);

        return await GetSyncStatusCoreAsync(scheduleId);
    }

    private async Task<SyncStatusDTO> GetSyncStatusCoreAsync(int scheduleId)
    {
        var slots = await _scheduleRepository.GetGroupSchedulesAsync(scheduleId);

        if (slots.Count == 0)
            return new SyncStatusDTO { IsInSync = true };

        var union = await _enrollmentRepository.GetEnrolledStudentIdsAsync(slots.Select(s => s.Id));
        var rosterSize = union.Count;

        var slotStatuses = slots
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.TimeStart)
            .Select(s => new SyncSlotDTO
            {
                ScheduleId = s.Id,
                Label = s.DayOfWeek.ToString()[..3]
                    + $" {s.TimeStart:HH\\:mm} - {s.TimeEnd:HH\\:mm}",
                StudentCount = s.StudentCount,
                // The union defines the roster, so "missing" is always relative to it.
                MissingCount = Math.Max(0, rosterSize - s.StudentCount)
            })
            .ToList();

        return new SyncStatusDTO
        {
            IsInSync = slotStatuses.All(x => x.MissingCount == 0),
            TotalSlots = slots.Count,
            RosterSize = rosterSize,
            Slots = slotStatuses
        };
    }

    /// <summary>
    /// Copies the group's roster — the union of students across every meeting slot — into
    /// every slot. Idempotent (ON DUPLICATE KEY), never removes students, and is a no-op
    /// for a class with no roster yet.
    /// <para>
    /// This is the system entry point with no user context: ClassScheduleService calls it
    /// after a schedule save so a newly added meeting picks up its siblings' roster. The
    /// UI "Sync now" path goes through <see cref="SyncNowAsync"/> so authorization still runs.
    /// </para>
    /// </summary>
    internal async Task<SyncResultDTO> SyncGroupEnrollmentAsync(int scheduleId)
    {
        var groupIds = await _scheduleRepository.GetGroupScheduleIdsAsync(scheduleId);

        if (groupIds.Count == 0)
            return new SyncResultDTO();

        var union = await _enrollmentRepository.GetEnrolledStudentIdsAsync(groupIds);

        if (union.Count == 0)
            return new SyncResultDTO();

        var countsBefore = await _enrollmentRepository.GetCountsAsync(groupIds);
        var slotsUpdated = groupIds.Count(id => countsBefore.GetValueOrDefault(id) < union.Count);

        await _enrollmentRepository.EnrollAsync(groupIds, union);

        return new SyncResultDTO
        {
            SlotsUpdated = slotsUpdated,
            RosterSize = union.Count
        };
    }

    /// <summary>
    /// Authorized "Sync now" from the roster panel: re-checks the caller may manage the
    /// class, then runs the union copy.
    /// </summary>
    public async Task<SyncResultDTO> SyncNowAsync(int scheduleId)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsManage);
        await EnsureCanManageAsync(scheduleId);

        return await SyncGroupEnrollmentAsync(scheduleId);
    }

    /// <summary>
    /// Deletes the whole class — every meeting slot — in one transaction: its enrollments are
    /// removed and the schedule rows are soft-deleted, so the class disappears from both the
    /// class list and the schedule page. Student records themselves are kept (they may be
    /// enrolled elsewhere and have lab logs). Returns the number of enrollments removed.
    /// </summary>
    public async Task<int> DeleteClassListAsync(int scheduleId)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsManage);
        await EnsureCanManageAsync(scheduleId);

        var groupIds = await _scheduleRepository.GetGroupScheduleIdsAsync(scheduleId);

        await using var connection = _dbFactory.Create();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var removed = await _enrollmentRepository.DeleteAllAsync(groupIds, connection, transaction);

            await _scheduleRepository.DeleteManyAsync(groupIds, connection, transaction);

            await transaction.CommitAsync();

            return removed;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Parses and classifies an import file for display. Nothing is written — the commit
    /// re-reads the file and re-computes the classification inside its transaction.
    /// </summary>
    public async Task<ClassListImportPreview> PreviewImportAsync(int scheduleId,
        Stream csv)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsManage);
        await EnsureCanManageAsync(scheduleId);

        var groupIds = await _scheduleRepository.GetGroupScheduleIdsAsync(scheduleId);
        var rows = _csvParser.Parse(csv);

        return await ClassifyAsync(groupIds, rows, connection: null, transaction: null);
    }

    /// <summary>
    /// Imports a CSV file in one transaction: students unknown to the students table are
    /// created, then everyone not yet in the class is enrolled into every meeting slot.
    /// The classification is recomputed from the file here, never trusted from the preview.
    /// Failure rolls back student creation and enrollment together.
    /// </summary>
    public async Task<ClassListImportResult> CommitImportAsync(int scheduleId,
        Stream csv)
    {
        await actor.RequirePrivilegeAsync(Privileges.ClassListsManage);
        await EnsureCanManageAsync(scheduleId);

        var groupIds = await _scheduleRepository.GetGroupScheduleIdsAsync(scheduleId);
        var rows = _csvParser.Parse(csv);

        await using var connection = _dbFactory.Create();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var preview = await ClassifyAsync(groupIds, rows, connection, transaction);

            var newStudents = preview.Rows
                .Where(r => r.Status == ClassListImportStatus.WillCreateStudent)
                .Select(r =>
                {
                    var student = new Student(
                        r.StudentId,
                        new Name(r.FirstName!),
                        new Name(r.LastName!));

                    if (!string.IsNullOrWhiteSpace(r.MiddleName))
                        student.SetMiddleName(new Name(r.MiddleName));

                    if (!string.IsNullOrWhiteSpace(r.Suffix))
                        student.SetSuffix(r.Suffix);

                    return student;
                })
                .ToList();

            if (newStudents.Count > 0)
                await _studentRepository.CreateManyAsync(newStudents, connection, transaction);

            var idsToEnroll = preview.Rows
                .Where(r => r.Status is ClassListImportStatus.WillEnroll or ClassListImportStatus.WillCreateStudent)
                .Select(r => r.StudentId)
                .Distinct()
                .ToList();

            if (idsToEnroll.Count > 0)
                await _enrollmentRepository.EnrollAsync(groupIds, idsToEnroll, connection, transaction);

            await transaction.CommitAsync();

            return new ClassListImportResult
            {
                // Enrolled counts students, not slot rows — a two-slot class still reports 38.
                Enrolled = preview.ToEnroll + preview.ToCreate,
                CreatedStudents = newStudents.Count,
                AlreadyEnrolled = preview.AlreadyEnrolled,
                Invalid = preview.Invalid
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Turns parsed rows into a preview: a status per row and the "name differs" note for
    /// existing students whose stored name disagrees with the file. Read-only.
    /// </summary>
    private async Task<ClassListImportPreview> ClassifyAsync(
        IReadOnlyList<int> groupIds,
        IReadOnlyList<ClassListImportRow> rows,
        DbConnection? connection,
        DbTransaction? transaction)
    {
        var validRows = rows.Where(r => r.Status != ClassListImportStatus.Invalid).ToList();
        var validIds = validRows.Select(r => r.StudentId).Distinct().ToList();

        var existingIds = validIds.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await _studentRepository.GetExistingIdsAsync(validIds, connection, transaction))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var enrolledIds = groupIds.Count == 0 || validIds.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await _enrollmentRepository.GetEnrolledStudentIdsAsync(groupIds, connection, transaction))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stored = existingIds.Count == 0
            ? new Dictionary<string, EnrolledStudentDTO>(StringComparer.OrdinalIgnoreCase)
            : (await _studentRepository.FindByIdsAsync(existingIds, connection, transaction))
                .ToDictionary(s => s.StudentId, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.Status == ClassListImportStatus.Invalid)
                continue;

            if (enrolledIds.Contains(row.StudentId))
            {
                row.Status = ClassListImportStatus.AlreadyEnrolled;
                continue;
            }

            if (existingIds.Contains(row.StudentId))
            {
                row.Status = ClassListImportStatus.WillEnroll;

                // Never overwrite an existing record; just surface the difference.
                if (stored.TryGetValue(row.StudentId, out var student)
                    && !string.Equals(student.DisplayName.Trim(), row.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    row.NameDiffers = true;
                    row.StoredName = student.DisplayName;
                }

                continue;
            }

            row.Status = ClassListImportStatus.WillCreateStudent;
        }

        return new ClassListImportPreview { Rows = rows };
    }

    /// <summary>Full name with a middle initial, mirroring EnrolledStudentDTO.DisplayName.</summary>
    private static string BuildDisplayName(
        string firstName,
        string lastName,
        string? middleName,
        string? suffix)
    {
        var middleInitial = string.IsNullOrWhiteSpace(middleName)
            ? null
            : $"{middleName.Trim()[0].ToString().ToUpperInvariant()}.";

        return string.Join(' ', new[] { firstName, middleInitial, lastName, suffix }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
