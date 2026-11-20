using System.Security.Claims;
using CLMS.Shared.Contracts;
using CLMS.Shared.Dtos;
using Microsoft.AspNetCore.Authentication;
using CLMS.Application.Identity.Services;

namespace CLMS.Web.Authentication;

public static class BrowserSignIn
{
    /// <summary>The restricted cookie is never the default scheme and cannot authorize normal app pages.</summary>
    public static async Task<string> CompleteAsync(HttpContext http, UserClaimsDto user, bool rememberMe = false)
    {
        await http.SignOutAsync(AuthenticationSchemes.Cookie);
        await http.SignOutAsync(AuthenticationSchemes.PasswordChange);
        await CancelPendingAsync(http);
        var login = http.RequestServices.GetRequiredService<EmailLoginService>();
        if (user.TwoFactorEnabled && !user.TwoFactorVerified)
        {
            var receipt = await login.BeginAsync(user, http.RequestAborted);
            await http.SignInAsync(AuthenticationSchemes.TwoFactor, new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("pending_credential", receipt.ChallengeCredential), new Claim("remember", rememberMe.ToString())], AuthenticationSchemes.TwoFactor)),
                new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(receipt.ExpiresAt), IsPersistent = false });
            return "/auth/two-factor";
        }
        await login.ValidateAsync(user);
        var scheme = user.PasswordChanged ? AuthenticationSchemes.Cookie : AuthenticationSchemes.PasswordChange;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()), new(ClaimTypes.Email, user.Email),
            new("security_stamp", user.SecurityStamp), new("principal_type", "human")
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

    /// <summary>Renews only the browser that just confirmed disabling 2FA; old sessions remain revoked.</summary>
    public static async Task RenewAfterDisablingAsync(HttpContext http, UserClaimsDto confirmed, AuthenticationProperties original)
    {
        if (confirmed.TwoFactorEnabled || !confirmed.TwoFactorVerified || !confirmed.PasswordChanged ||
            original.ExpiresUtc is null || original.ExpiresUtc <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Sign in again.");
        await http.RequestServices.GetRequiredService<EmailLoginService>().ValidateAsync(confirmed);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, confirmed.UserId.ToString()), new(ClaimTypes.Email, confirmed.Email),
            new(ClaimTypes.Role, confirmed.UserRole.ToString()), new("security_stamp", confirmed.SecurityStamp), new("principal_type", "human")
        };
        var properties = new AuthenticationProperties(new Dictionary<string, string?>(original.Items), new Dictionary<string, object?>(original.Parameters));
        await http.SignInAsync(AuthenticationSchemes.Cookie,
            new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemes.Cookie)), properties);
    }

    public static async Task CancelPendingAsync(HttpContext http)
    {
        var pending = await http.AuthenticateAsync(AuthenticationSchemes.TwoFactor);
        if (pending.Principal?.FindFirstValue("pending_credential") is { } credential)
            http.RequestServices.GetRequiredService<EmailLoginService>().Cancel(credential);
        await http.SignOutAsync(AuthenticationSchemes.TwoFactor);
    }

    // All Google identity values come from the protected ticket created by the OAuth middleware.
    public static ExternalLoginRequest GoogleIdentity(ClaimsPrincipal principal) => new("google",
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", principal.FindFirstValue(ClaimTypes.Email) ?? "",
        bool.TryParse(principal.FindFirstValue("google:email_verified"), out var verified) && verified,
        principal.FindFirstValue("google:hd"));
}
