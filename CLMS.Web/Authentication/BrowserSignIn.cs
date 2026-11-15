using System.Security.Claims;
using CLMS.Shared.Contracts;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Authentication;

namespace CLMS.Web.Authentication;

public static class BrowserSignIn
{
    /// <summary>The restricted cookie is never the default scheme and cannot authorize normal app pages.</summary>
    public static async Task<string> CompleteAsync(HttpContext http, UserClaimsDto user, bool rememberMe = false)
    {
        await http.SignOutAsync(AuthenticationSchemes.Cookie);
        await http.SignOutAsync(AuthenticationSchemes.PasswordChange);
        var scheme = user.PasswordChanged ? AuthenticationSchemes.Cookie : AuthenticationSchemes.PasswordChange;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()), new(ClaimTypes.Email, user.Email)
        };
        if (user.PasswordChanged) claims.Add(new(ClaimTypes.Role, user.UserRole.ToString()));
        await http.SignInAsync(scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)), new AuthenticationProperties
        {
            IsPersistent = user.PasswordChanged && rememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(!user.PasswordChanged ? TimeSpan.FromMinutes(10) :
                rememberMe ? TimeSpan.FromDays(5) : TimeSpan.FromHours(8))
        });
        return user.PasswordChanged ? "/" : "/account/change-password";
    }

    // All Google identity values come from the protected ticket created by the OAuth middleware.
    public static ExternalLoginRequest GoogleIdentity(ClaimsPrincipal principal) => new("google",
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", principal.FindFirstValue(ClaimTypes.Email) ?? "",
        bool.TryParse(principal.FindFirstValue("google:email_verified"), out var verified) && verified,
        principal.FindFirstValue("google:hd"));
}
