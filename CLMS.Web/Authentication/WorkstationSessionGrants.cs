using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace CLMS.Web.Authentication;

public sealed record WorkstationSessionIdentity(Guid UserId, string SecurityStamp);

public sealed class WorkstationSessionGrants(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("CLMS.WorkstationSession.Start.v1");

    public string Issue(Guid unitId, Guid sessionId, Guid userId, string stamp) =>
        protector.Protect(JsonSerializer.Serialize(new Grant(unitId, sessionId, userId, stamp,
            clock.GetUtcNow().AddMinutes(2))));

    public WorkstationSessionIdentity? Read(string grant, Guid unitId, Guid sessionId)
    {
        if (string.IsNullOrWhiteSpace(grant) || grant.Length > 2048) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Grant>(protector.Unprotect(grant));
            return value is not null && value.UnitId == unitId && value.SessionId == sessionId &&
                value.ExpiresAt > clock.GetUtcNow()
                ? new(value.UserId, value.SecurityStamp) : null;
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed record Grant(Guid UnitId, Guid SessionId, Guid UserId, string SecurityStamp, DateTimeOffset ExpiresAt);
}
