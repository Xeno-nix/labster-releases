namespace CLMS.Agent.Services;

public interface IAgentSetupStore
{
    string ServerAddress { get; }
    Task<string?> GetDeviceKeyAsync();
}
