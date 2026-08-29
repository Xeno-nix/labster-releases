namespace CLMS.Domain.Entities.UtilizationLogging;

/// <summary>
/// Mirrors the Laravel `lab_utilization_logs` table. Exactly one of StudentId / GuestLogId
/// is set per row (student session or visitor session), matching the DB's
/// chk_student_or_guest constraint. An open session is one where TimeOut is null.
///
/// LaboratoryId / InstructorId / ClassScheduleId are nullable for now: resolving them
/// requires reading the booking module's ClassSchedule/Booking data (student's active
/// class, or the visitor's approved reservation), which isn't wired up yet. Once that
/// lookup exists, LoggingService should populate these instead of leaving them null.
/// Note the live `lab_utilization_logs.lab_id` column is currently NOT NULL, so until
/// this is wired up, real inserts against that schema will need a value supplied some
/// other way (or the column constraint revisited) — flagging for whoever picks this up.
/// </summary>
public class LaboratoryUseLog
{
    public int Id { get; set; }

    public string? StudentId { get; set; }
    public int? GuestLogId { get; set; }

    public string Purpose { get; set; } = string.Empty;
    public int? InstructorId { get; set; }
    public int? LaboratoryId { get; set; }

    /// <summary>Forward-compat hook: the class_schedule row this session was tied to, once resolvable.</summary>
    public int? ClassScheduleId { get; set; }

    public DateOnly LogDate { get; set; }
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }

    public bool IsActive => TimeOut is null;
}
