using System.Security.Claims;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CLMS.Web.Authentication;

public sealed class HumanSessionValidator(IUserRepository users, IAccountSecurityCrypto crypto)
{
    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirstValue("principal_type") != "human" ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var user = await users.FindByIdAsync(id);
        return user is not null && CLMS.Application.Authorization.Privileges.IsStaff(user.UserRole) && !string.IsNullOrWhiteSpace(user.PasswordHash) && !user.DeletedAt.HasValue && !user.IsLockedOut &&
            crypto.StampMatches(user, principal.FindFirstValue("security_stamp") ?? "") &&
            (principal.Identity.AuthenticationType == AuthenticationSchemes.PasswordChange || user.PasswordChanged);
    }
    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null || !await context.HttpContext.RequestServices.GetRequiredService<HumanSessionValidator>().IsCurrentAsync(context.Principal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }
    public static async Task ValidateBearerAsync(TokenValidatedContext context)
    {
        await AgentAuthentication.RequireHuman(context);
        if (context.Principal is null || !await context.HttpContext.RequestServices.GetRequiredService<HumanSessionValidator>().IsCurrentAsync(context.Principal))
            context.Fail("Sign in again.");
    }
}
