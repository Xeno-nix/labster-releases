namespace CLMS.Domain.Entities.UtilizationLogging;

/// <summary>
/// Mirrors the `attendance_logs` table (see Initial_Database_Setup.txt). Exactly one of
/// StudentId / GuestId is set per row (student session or visitor session). An open
/// session is one where TimeOut is null.
///
/// Note: laboratory_id was added by the 2026-09-23 migration and is persisted on
/// insert (rows created before that migration have NULL lab — no backfill is possible).
/// ClassScheduleId below is still NOT a persisted column; it's resolved transiently
/// (e.g. from the student's class schedule) purely to build a meaningful Purpose string
/// and for in-memory use during a single request.
/// </summary>
public class LaboratoryUseLog
{
    public int Id { get; set; }

    public string? StudentId { get; set; }
    public int? GuestId { get; set; }

    /// <summary>Maps to the `purpose_subject` column.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Maps to the `faculty_in_charge` column (a users.user_id GUID stored as text).</summary>
    public Guid? InstructorId { get; set; }

    /// <summary>Not persisted — see class remarks.</summary>
    public int? LaboratoryId { get; set; }

    /// <summary>Not persisted — see class remarks.</summary>
    public int? ClassScheduleId { get; set; }

    public DateOnly LogDate { get; set; }
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }

    public bool IsActive => TimeOut is null;
}
