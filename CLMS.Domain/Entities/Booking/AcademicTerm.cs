using CLMS.Domain.Enums;

namespace CLMS.Domain.Entities.Booking;

public class AcademicTerm
{
    public int Id {get; set; }
    public string AcademicYear{ get; set; }
    public Semester Semester { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

}