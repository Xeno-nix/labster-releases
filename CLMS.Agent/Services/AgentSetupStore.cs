using CLMS.Agent.Configuration;
using Microsoft.Maui.Storage;

namespace CLMS.Agent.Services;

public sealed class AgentSetupStore : IAgentSetupStore
{
    private const string ServerKey = "agent.server-address";
    private const string DeviceKey = "agent.device-key";

    public string ServerAddress => Preferences.Default.Get(ServerKey, ServerOptions.DefaultAddress);

    public Task<string?> GetDeviceKeyAsync() => SecureStorage.Default.GetAsync(DeviceKey);

    public async Task SaveAsync(string serverAddress, string deviceKey)
    {
        serverAddress = serverAddress.Trim().TrimEnd('/');
        if (!Uri.TryCreate(serverAddress, UriKind.Absolute, out var server)
            || server.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(server.UserInfo)
            || !string.IsNullOrEmpty(server.Query) || !string.IsNullOrEmpty(server.Fragment))
            throw new ArgumentException("Enter an HTTPS server address, for example https://localhost:7190.");

        deviceKey = deviceKey.Trim();
        if (deviceKey.Length is < 32 or > 512)
            throw new ArgumentException("Enter the device key provisioned for this unit (at least 32 characters).");

        // Never persist the credential in ordinary preferences.
        await SecureStorage.Default.SetAsync(DeviceKey, deviceKey);
        Preferences.Default.Set(ServerKey, serverAddress);
    }
}
