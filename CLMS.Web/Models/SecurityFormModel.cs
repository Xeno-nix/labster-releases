namespace CLMS.Web.Models;

public sealed record SecurityFormModel(string Mode, string? Error = null, string? Message = null,
    Guid UserId = default, string Token = "", DateTime? ExpiresAt = null, DateTime? ResendAfter = null)
{
    public string Email { get; init; } = "";
    public DateTimeOffset? RecoveryRetryAt { get; init; }
    public DateTimeOffset? RecoveryServerTime { get; init; }
    public int RecoveryWaitSeconds => RecoveryRetryAt is { } retry && RecoveryServerTime is { } now
        ? Math.Max(0, (int)Math.Ceiling((retry - now).TotalSeconds)) : 0;
}
