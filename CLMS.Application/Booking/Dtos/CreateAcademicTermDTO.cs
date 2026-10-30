using System.ComponentModel.DataAnnotations;
using CLMS.Domain.Enums;

namespace CLMS.Application.DTOs.Booking;

public class CreateAcademicTermDTO : IValidatableObject
{
    [Required, RegularExpression(@"^\d{4}-\d{4}$", ErrorMessage = "Use the school year format 2026-2027.")]
    public string AcademicYear { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(Semester))]
    public Semester? Semester { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var years = AcademicYear.Split('-');
        if (years.Length == 2 && int.TryParse(years[0], out var start) && int.TryParse(years[1], out var end)
            && end != start + 1)
            yield return new ValidationResult("The school year must span consecutive years.", [nameof(AcademicYear)]);

        if (StartDate.HasValue && EndDate.HasValue && EndDate < StartDate)
            yield return new ValidationResult("End date must be on or after the start date.", [nameof(EndDate)]);
    }
}
