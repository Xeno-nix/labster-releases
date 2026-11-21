using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Services;

public sealed class EmailLoginService(IEmailLoginChallenges challenges, IUserRepository users,
    IAccountSecurityCrypto crypto, IEmailService email)
{
    public async Task<EmailLoginReceipt> BeginAsync(UserClaimsDto firstFactor, CancellationToken ct = default)
    {
        await ValidateAsync(firstFactor);
        var delivery = challenges.Create(firstFactor);
        await SendAsync(delivery, ct);
        return delivery.Receipt;
    }
    public async Task<EmailLoginReceipt> ResendAsync(string credential, CancellationToken ct = default)
    {
        var delivery = challenges.Resend(credential);
        await ValidateAsync(delivery.User);
        await SendAsync(delivery, ct);
        return delivery.Receipt;
    }
    public async Task<UserClaimsDto> VerifyAsync(VerifyLoginCodeDto request)
    {
        var user = challenges.Consume(request.ChallengeCredential, request.Code);
        await ValidateAsync(user);
        return user;
    }
    public EmailLoginReceipt Status(string credential) => challenges.Status(credential);
    public void Cancel(string credential) => challenges.Cancel(credential);
    public async Task ValidateAsync(UserClaimsDto claims)
    {
        var current = await users.FindByIdAsync(claims.UserId);
        if (current is null || !CLMS.Application.Authorization.Privileges.IsStaff(current.UserRole) || current.DeletedAt.HasValue || current.IsLockedOut || !crypto.StampMatches(current, claims.SecurityStamp))
            throw new UnauthorizedAccessException("The account changed or is unavailable. Sign in again.");
    }
    private async Task SendAsync(LoginCodeDelivery delivery, CancellationToken ct)
    {
        try
        {
            await email.SendEmailAsync(delivery.User.Email, "Your Labster sign-in code",
                "<p>Your sign-in code is <strong>" + delivery.Code + "</strong>.</p><p>It expires at " +
                delivery.Receipt.ExpiresAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " UTC. Do not share it.</p>", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { challenges.Cancel(delivery.Receipt.ChallengeCredential); throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            challenges.Cancel(delivery.Receipt.ChallengeCredential);
            throw new InvalidOperationException("The verification email could not be sent. Try signing in again shortly.");
        }
    }
}
