using System.ComponentModel.DataAnnotations;

namespace CLMS.Application.Logging.Dtos;

/// <summary>Mirrors Laravel's StudentTimeInRequest.</summary>
public class StudentTimeInRequest
{
    [Required(ErrorMessage = "The student number field is required.")]
    [Display(Name = "Student Number")]
    public string StudentId { get; set; } = string.Empty;
}
