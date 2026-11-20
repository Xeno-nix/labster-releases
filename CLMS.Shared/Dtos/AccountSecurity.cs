namespace CLMS.Shared.Dtos;

public sealed record EmailLoginReceipt(string ChallengeCredential, DateTime ExpiresAt, DateTime ResendAfter);
public sealed record VerifyLoginCodeDto(string ChallengeCredential, string Code);
public sealed record ResendLoginCodeDto(string ChallengeCredential);
public sealed record ResetPasswordDto(Guid UserId, string Token, string NewPassword, string ConfirmPassword);
public sealed record TwoFactorSettingsStatus(string Email, bool Enabled);
public sealed record TwoFactorSettingReceipt(EmailLoginReceipt Challenge, bool Enabled);
public sealed record ConfirmTwoFactorSettingDto(string ChallengeCredential, string Code, string CurrentPassword);
