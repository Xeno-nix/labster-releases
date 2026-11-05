namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// A "class" as the UI presents it: one or more weekly meetings of the same course,
/// taught by the same instructor, in the same term, at the same year level and section.
/// A class that meets twice a week is a single ClassListClassDTO with two Slots.
/// <para>
/// When YearLevel or Section is null the schedule cannot be grouped with its siblings, so
/// it forms its own group with a single slot. See <see cref="IsGrouped"/>.
/// </para>
/// </summary>
public class ClassListClassDTO
{
    /// <summary>
    /// A representative schedule id used for authorization and as the canonical roster
    /// holder. Always one of <see cref="Slots"/>.
    /// </summary>
    public int RepresentativeScheduleId { get; set; }

    public int CourseId { get; set; }

    public string CourseCode { get; set; } = string.Empty;

    public string CourseName { get; set; } = string.Empty;

    public Guid InstructorId { get; set; }

    public string InstructorName { get; set; } = string.Empty;

    public int AcademicTermId { get; set; }

    public string AcademicYear { get; set; } = string.Empty;

    public string Semester { get; set; } = string.Empty;

    public int? YearLevel { get; set; }

    public string? Section { get; set; }

    public IReadOnlyList<ClassSlotDTO> Slots { get; set; } = [];

    /// <summary>
    /// False when YearLevel or Section is null, meaning this schedule has no siblings to
    /// merge with. The UI shows a "Set year/section" hint in that case.
    /// </summary>
    public bool IsGrouped => YearLevel.HasValue && !string.IsNullOrWhiteSpace(Section);

    /// <summary>e.g. "BSIT 2-A", or null when year level / section are not set.</summary>
    public string? YearSectionLabel => IsGrouped
        ? $"{CourseCode} {YearLevel}-{Section}"
        : null;

    /// <summary>
    /// Roster size for the class, taken from the representative slot. A grouped class whose
    /// slots disagree is out of sync; the sync badge surfaces that separately.
    /// </summary>
    public int StudentCount { get; set; }

    /// <summary>e.g. "Mon 08:00 - 10:00, Thu 08:00 - 10:00"</summary>
    public string SlotsLabel => string.Join(", ", Slots.Select(s => s.Label));
}