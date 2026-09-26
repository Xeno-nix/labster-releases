namespace CLMS.Application.DTOs.Booking;

public class AcademicTermDTO
{
    public int Id { get; set; }
    public string AcademicYear{ get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}