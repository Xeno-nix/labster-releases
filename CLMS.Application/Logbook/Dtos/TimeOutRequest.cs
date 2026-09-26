using System.ComponentModel.DataAnnotations;

namespace CLMS.Application.Logging.Dtos;

/// <summary>Mirrors Laravel's TimeOutRequest.</summary>
public class TimeOutRequest
{
    [Required(ErrorMessage = "Please enter your student number or full name.")]
    [StringLength(150)]
    [Display(Name = "Student Number or Full Name")]
    public string Identifier { get; set; } = string.Empty;
}
