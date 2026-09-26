using System.ComponentModel.DataAnnotations;

namespace CLMS.Web.Models;

public class LoginFormModel
{
    [Required(ErrorMessage = "Email field is empty.")]
    [EmailAddress(ErrorMessage = "Email is not valid.")]
    public string Email { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Password field empty.")]
    public string Password { get; set; } = string.Empty;
}