using System.ComponentModel.DataAnnotations;

namespace CLMS.Application.DTOs.Booking;

public class CreateCourseDTO
{
    [Required, StringLength(50)]
    public string CourseCode { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string CourseName { get; set; } = string.Empty;
}
