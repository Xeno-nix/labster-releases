using CLMS.Agent.Core;

namespace CLMS.Agent.Services;

/// <summary>Shares only memory-resident access tokens with the transport, never with settings.</summary>
public interface IAgentAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Portable enrollment lifecycle owned by the application runtime.</summary>
public interface IAgentIdentityRuntime : IAgentAccessTokenProvider
{
    AgentStatus Status { get; }
    TimeSpan NextDelay { get; }
    Task InitializeAsync(CancellationToken ct);
    Task TickAsync(CancellationToken ct);
}
