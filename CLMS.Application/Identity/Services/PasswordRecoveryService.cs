using System.Net;
using System.Net.Mail;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Services;

public sealed class PasswordRecoveryService(IUserRepository users, IResetTokenRepository resets,
    IAccountSecurityCrypto crypto, IPasswordHasher passwords, IPasswordRecoveryQueue queue,
    IEmailService email, AccountOptions options, TimeProvider clock)
{
    public PasswordRecoveryRequestResult Request(string emailAddress)
    {
        var address = (emailAddress ?? "").Trim().ToLowerInvariant();
        if (address.Length > 100 || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
            throw new InvalidOperationException("Enter a valid email address.");
        PublicUrl();
        return queue.Enqueue(address);
    }

    /// <summary>The worker resolves the address after the public response; failed delivery never reveals account existence.</summary>
    public async Task SendAsync(string address, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(address);
        if (user is null || user.DeletedAt.HasValue) return;
        var token = crypto.GenerateToken();
        var reset = new ResetToken(user.Id, crypto.Hash(token), clock.GetUtcNow().UtcDateTime.AddMinutes(30)) { Email = user.Email };
        if (!await resets.CreateAsync(reset)) return;
        var url = PublicUrl() + "/reset-password?userId=" + user.Id.ToString("D") + "&token=" + Uri.EscapeDataString(token);
        await email.SendEmailAsync(user.Email, "Reset your Labster password", "<p>A password reset was requested for your account.</p><p><a href=\"" +
            WebUtility.HtmlEncode(url) + "\">Reset password</a></p><p>This link expires in 30 minutes. If you did not request it, ignore this email.</p>", ct);
    }

    public async Task ResetAsync(ResetPasswordDto request, CancellationToken ct = default)
    {
        ValidatePassword(request.NewPassword, request.ConfirmPassword);
        if (request.Token is not { Length: 43 } || !request.Token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw InvalidLink();
        var reset = await resets.FindByUserIdAsync(request.UserId.ToString("D"));
        if (reset is null || reset.ExpiresAt <= clock.GetUtcNow().UtcDateTime || !crypto.Verify(request.Token, reset.TokenHash)) throw InvalidLink();
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null || user.DeletedAt.HasValue) throw InvalidLink();
        if (passwords.Verify(request.NewPassword, user.PasswordHash))
            throw new InvalidOperationException("Choose a password different from your current password.");
        if (!await resets.ConsumeAsync(request.UserId, reset.TokenHash, user.PasswordHash, passwords.Hash(request.NewPassword), ct)) throw InvalidLink();
    }
    public static void ValidatePassword(string password, string confirmation)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 1024)
            throw new InvalidOperationException("Choose a password of 8–1024 characters.");
        if (password != confirmation) throw new InvalidOperationException("The passwords do not match.");
    }
    private string PublicUrl()
    {
        if (!Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != "https" && (options.RequireHttps || uri.Scheme != "http")))
            throw new InvalidOperationException("Configure the public HTTPS URL for password recovery.");
        return options.PublicUrl.TrimEnd('/');
    }
    private static InvalidOperationException InvalidLink() => new("This reset link is invalid or expired. Request a new one.");
}
