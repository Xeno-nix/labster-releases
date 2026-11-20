using Microsoft.AspNetCore.Authentication;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.ScreenCasting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.SignalR;

namespace CLMS.Web.Services.AgentConnections;

public static class AgentConnectionSetup
{
    public static IServiceCollection AddAgentConnections(this IServiceCollection services, IConfiguration configuration)
    {
        // Device JWTs use the same persistent registry authority as the REST APIs.
        services.AddAuthentication().AddJwtBearer(AuthenticationSchemes.AgentBearer,
            options => AgentAuthentication.Configure(options, configuration));
        services.AddAuthorization(AgentAuthentication.AddPolicies);
        services.AddScoped<AgentCredentialHubFilter>();
        services.AddSignalR().AddHubOptions<AgentConnectionHub>(options =>
        {
            options.MaximumReceiveMessageSize = ScreenCastProtocol.MaximumHubMessageBytes;
            options.AddFilter<AgentCredentialHubFilter>();
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AgentPresenceStore>();
        services.AddScoped<WorkstationCommandService>();
        services.AddSingleton<CLMS.Web.Services.Notifications.BrowserNotificationBus>();
        services.AddScoped<CLMS.Web.Services.Notifications.SystemNotificationPublisher>();
        services.AddSingleton<ScreenCastService>();
        services.AddHostedService(provider => provider.GetRequiredService<ScreenCastService>());
        services.AddSingleton<FacultyStreamService>();
        services.TryAddSingleton<LabScreenCastService>();
        services.AddHostedService(provider => provider.GetRequiredService<FacultyStreamService>());
        return services;
    }
}
