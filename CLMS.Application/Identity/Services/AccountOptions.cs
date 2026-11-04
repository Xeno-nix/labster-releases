namespace CLMS.Application.Identity.Services;

/// <summary>Uses the existing Onboarding configuration section to avoid duplicating deployment settings.</summary>
public sealed class AccountOptions
{
    public string PublicUrl { get; set; } = "";
    /// <summary>The host disables this only in Development, after binding configuration.</summary>
    public bool RequireHttps { get; set; } = true;
    public string SupportEmail { get; set; } = "";
    public string[] AllowedWorkspaceDomains { get; set; } = ["usep.edu.ph"];
}
