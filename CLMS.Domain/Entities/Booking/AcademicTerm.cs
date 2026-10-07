namespace CLMS.Domain.Entities.Booking;
using CLMS.Domain.Enums;

public class AcademicTerm
{
    public int Id {get; set; }
    public string AcademicYear{ get; set; }
    public Semester Semester { get; set; }

    /// <summary>
    /// Nullable: academic_terms.start_date / end_date are nullable in the schema, and a
    /// term can be created before its dates are known. A NULL bound is treated as open-ended.
    /// </summary>
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

}