using CLMS.Shared.Dtos;

namespace CLMS.Agent.Core;

// This entire snapshot belongs in the service vault, never in UI preferences or IPC.
public sealed record AgentState(
    string? ServerAddress = null, 
    RegistrationReceipt? Registration = null,
    string Phase = "RegistrationRequired", 
    string? KeyId = null, 
    DeviceIdentity? Identity = null,
    KeyRotationState? Rotation = null, 
    string? RetiredKeyId = null, 
    int Version = 2, 
    IReadOnlyList<string>? RetiredKeys = null
    );

public sealed record KeyRotationState(Guid OperationId, string NewKeyId, DeviceIdentity? Replacement = null);
public sealed record AgentStatus(string Phase, string Message, string? ServerAddress = null,
    string? RegistrationCode = null, DateTime? ExpiresAtUtc = null, Guid? ComputerUnitId = null,
    bool CanConfigure = false, bool CanRegister = false, bool CanRetry = false);

public interface IAgentVault
{
    /// <summary>Loads the complete credential snapshot from the service-owned OS vault, returning null when no snapshot exists.</summary>
    Task<AgentState?> ReadAsync(CancellationToken ct);
    /// <summary>Durably replaces the complete service snapshot without exposing credentials to the UI.</summary>
    Task WriteAsync(AgentState state, CancellationToken ct);
}

public sealed class AgentApiException(int status, string code, TimeSpan? retryAfter = null) : Exception("Agent API request failed.")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
