using CLMS.Agent.Models;

namespace CLMS.Agent.Services;

public interface IAgentConnectionTransport
{
    bool IsRunning { get; }
    AgentConnectionSnapshot Snapshot { get; }
    Task ConnectAsync(string serverAddress, string deviceKey, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
