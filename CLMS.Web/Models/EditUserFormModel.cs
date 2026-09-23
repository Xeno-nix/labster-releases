namespace CLMS.Web.Models;

using System.ComponentModel.DataAnnotations;
using CLMS.Domain.ValueObjects;
using CLMS.Domain.Enums;

public sealed class EditUserModel
{
    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = String.Empty;
    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = String.Empty;
    public string? MiddleName { get; set; }
    public string? Suffix { get; set; }
    public Role UserRole { get; set; }
}