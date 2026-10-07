namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// One weekly meeting of a class (one class_schedule row).
/// </summary>
public class ClassSlotDTO
{
    public int ScheduleId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly TimeStart { get; set; }

    public TimeOnly TimeEnd { get; set; }

    public int LaboratoryId { get; set; }

    public string LaboratoryName { get; set; } = string.Empty;

    /// <summary>
    /// Students enrolled in THIS slot. After a successful sync this equals the class roster
    /// size; a lower number means the slot is out of sync.
    /// </summary>
    public int StudentCount { get; set; }

    /// <summary>e.g. "Mon 08:00 - 10:00"</summary>
    public string Label => $"{DayOfWeek.ToString()[..3]} {TimeStart:HH\\:mm} - {TimeEnd:HH\\:mm}";
}