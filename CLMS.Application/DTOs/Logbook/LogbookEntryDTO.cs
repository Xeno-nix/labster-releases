namespace CLMS.Application.DTOs.Logbook;

/// <summary>
/// One row of the admin logbook table. Mirrors the $entries mapping in
/// LogbookController::logs().
///
/// NOTE: unlike the Laravel source (lab_utilization_logs.lab_id NOT NULL), the current
/// `attendance_logs` table has no lab_id column at all — see remarks on
/// CLMS.Domain.Entities.UtilizationLogging.LaboratoryUseLog. There is therefore no
/// reliable way to show which laboratory a past session happened in, so that column
/// (and the corresponding lab filter dropdown) is intentionally omitted here rather
/// than guessed at. If per-session laboratory tracking is needed, attendance_logs
/// would need a lab_id column added — flagging for that decision rather than making it.
/// </summary>
public class LogbookEntryDTO
{
    public int Id { get; set; }

    /// <summary>The student's ID, or "GUEST" for visitor sessions — matches Laravel's fallback.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Student full name, guest name, or "—" if neither is available.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Purpose { get; set; }

    public DateOnly? Date { get; set; }
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }

    public bool IsActive => TimeOut is null;
}
