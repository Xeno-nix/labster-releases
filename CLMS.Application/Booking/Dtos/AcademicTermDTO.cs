namespace CLMS.Application.DTOs.Booking;

public class AcademicTermDTO
{
    public int Id { get; set; }
    public string AcademicYear{ get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;

    // Nullable to match academic_terms: start_date / end_date may not be set yet.
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}