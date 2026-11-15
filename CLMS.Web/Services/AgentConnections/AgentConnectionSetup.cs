using Microsoft.AspNetCore.Authentication;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.ScreenCasting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CLMS.Web.Services.AgentConnections;

public static class AgentConnectionSetup
{
    public static IServiceCollection AddAgentConnections(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentConnectionOptions>(configuration.GetSection(AgentConnectionOptions.SectionName));
        // No default scheme is changed; browser cookies and staff JWT authentication stay separate.
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(
            AgentKeyAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddSignalR().AddHubOptions<AgentConnectionHub>(options =>
            options.MaximumReceiveMessageSize = ScreenCastProtocol.MaximumHubMessageBytes);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AgentPresenceStore>();
        services.AddSingleton<ScreenCastService>();
        services.AddHostedService(provider => provider.GetRequiredService<ScreenCastService>());
        return services;
    }
}
