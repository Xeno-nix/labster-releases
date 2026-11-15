namespace CLMS.Web.Models;

// Never put passwords or provider subjects in the view model; failed submissions render empty inputs.
public sealed record AccountFormViewModel(string Email, bool PasswordChangeRequired = false, string? Error = null);
