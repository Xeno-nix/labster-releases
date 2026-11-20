using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Services;

/// <summary>Changes only the authenticated account's preference after purpose-bound email and password proof.</summary>
public sealed class TwoFactorSettingsService(IUserRepository users, IEmailLoginChallenges challenges,
    IAccountSecurityCrypto crypto, IPasswordHasher passwords, IEmailService email)
{
    public async Task<TwoFactorSettingsStatus> StatusAsync(Guid userId, string stamp)
    {
        var user = await RequireUserAsync(userId, stamp);
        return new(user.Email, user.TwoFactorEnabled);
    }

    public async Task<TwoFactorSettingReceipt> RequestAsync(Guid userId, string stamp, bool enabled, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, stamp);
        if (user.TwoFactorEnabled == enabled) throw new InvalidOperationException("This setting is already saved. Reload account settings.");
        var delivery = challenges.CreateSetting(new(user.Id, user.Email, user.UserRole, user.PasswordChanged)
            { SecurityStamp = stamp, TwoFactorEnabled = user.TwoFactorEnabled }, enabled);
        await SendAsync(delivery, ct);
        return new(delivery.Delivery.Receipt, enabled);
    }

    public async Task<TwoFactorSettingReceipt> PendingAsync(Guid userId, string stamp, string credential)
    {
        await RequireUserAsync(userId, stamp);
        return challenges.SettingStatus(userId, credential);
    }

    public async Task<TwoFactorSettingReceipt> ResendAsync(Guid userId, string stamp, string credential, CancellationToken ct = default)
    {
        var current = await RequireUserAsync(userId, stamp);
        var delivery = challenges.ResendSetting(userId, credential);
        if (!crypto.StampMatches(current, delivery.Delivery.User.SecurityStamp)) throw new UnauthorizedAccessException("Sign in again.");
        await SendAsync(delivery, ct);
        return new(delivery.Delivery.Receipt, delivery.Enabled);
    }

    public async Task<UserClaimsDto> ConfirmAsync(Guid userId, string stamp, ConfirmTwoFactorSettingDto request, CancellationToken ct = default)
    {
        var current = await RequireUserAsync(userId, stamp);
        var passwordValid = request.CurrentPassword is { Length: > 0 and <= 1024 } && passwords.Verify(request.CurrentPassword, current.PasswordHash);
        // Incorrect passwords and codes share the same five-attempt budget. A proof cannot change another user or purpose.
        var proof = challenges.ConsumeSetting(userId, request.ChallengeCredential, request.Code, passwordValid);
        if (!crypto.StampMatches(current, proof.User.SecurityStamp)) throw new UnauthorizedAccessException("The account changed. Sign in again.");
        if (current.TwoFactorEnabled == proof.Enabled) throw new InvalidOperationException("This setting is already saved. Reload account settings.");
        // A new salt invalidates old fingerprints permanently, including when the preference is later toggled back.
        var replacementHash = passwords.Hash(request.CurrentPassword);
        if (!await users.ChangeTwoFactorAsync(current, proof.Enabled, replacementHash, ct))
            throw new UnauthorizedAccessException("The account changed. Sign in again.");
        // Return exactly our committed snapshot. Re-reading could adopt an unrelated concurrent password reset.
        current.PasswordHash = replacementHash;
        current.TwoFactorEnabled = proof.Enabled;
        return new(current.Id, current.Email, current.UserRole, current.PasswordChanged)
            { SecurityStamp = crypto.Stamp(current), TwoFactorEnabled = proof.Enabled, TwoFactorVerified = true };
    }

    public async Task CancelAsync(Guid userId, string stamp, string credential)
    {
        await PendingAsync(userId, stamp, credential);
        challenges.Cancel(credential);
    }

    private async Task<User> RequireUserAsync(Guid userId, string stamp)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.DeletedAt.HasValue || user.IsLockedOut || !user.PasswordChanged || !crypto.StampMatches(user, stamp))
            throw new UnauthorizedAccessException("Sign in again before changing account security.");
        return user;
    }

    private async Task SendAsync(SettingCodeDelivery delivery, CancellationToken ct)
    {
        try
        {
            await email.SendEmailAsync(delivery.Delivery.User.Email, "Confirm your Labster 2FA setting",
                "<p>Your code to " + (delivery.Enabled ? "enable" : "disable") + " email two-factor authentication is <strong>" +
                delivery.Delivery.Code + "</strong>.</p><p>It expires at " +
                delivery.Delivery.Receipt.ExpiresAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
                " UTC. Do not share it. If you did not request this change, do not approve it.</p>", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { challenges.Cancel(delivery.Delivery.Receipt.ChallengeCredential); throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            challenges.Cancel(delivery.Delivery.Receipt.ChallengeCredential);
            throw new InvalidOperationException("The verification email could not be sent. The setting was not changed. Try again shortly.");
        }
    }
}
