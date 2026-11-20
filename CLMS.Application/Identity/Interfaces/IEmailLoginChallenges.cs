using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Interfaces;

public sealed record LoginCodeDelivery(EmailLoginReceipt Receipt, UserClaimsDto User, string Code);
public sealed record SettingCodeDelivery(LoginCodeDelivery Delivery, bool Enabled);
public sealed record VerifiedSettingProof(UserClaimsDto User, bool Enabled);

/// <summary>One server instance owns pending proofs. Implementations must synchronize attempts, resends, and consumption.</summary>
public interface IEmailLoginChallenges
{
    LoginCodeDelivery Create(UserClaimsDto user);
    LoginCodeDelivery Resend(string credential);
    EmailLoginReceipt Status(string credential);
    UserClaimsDto Consume(string credential, string code);
    void Cancel(string credential);
    SettingCodeDelivery CreateSetting(UserClaimsDto user, bool enabled);
    SettingCodeDelivery ResendSetting(Guid userId, string credential);
    TwoFactorSettingReceipt SettingStatus(Guid userId, string credential);
    VerifiedSettingProof ConsumeSetting(Guid userId, string credential, string code, bool passwordValid);
}
