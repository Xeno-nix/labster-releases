namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// Outcome of one CSV row after preview classification against the students table and the
/// class's current roster.
/// </summary>
public enum ClassListImportStatus
{
    /// <summary>Student exists and is not yet in this class — will be enrolled on commit.</summary>
    WillEnroll,

    /// <summary>Student id is unknown — a new students row is created, then enrolled.</summary>
    WillCreateStudent,

    /// <summary>Student is already on this class's roster — skipped.</summary>
    AlreadyEnrolled,

    /// <summary>Row failed validation (bad id, bad name, duplicate in file...) — skipped.</summary>
    Invalid
}