using Microsoft.Extensions.DependencyInjection;
using CLMS.Application.Logging;
using CLMS.Domain.Services;

namespace CLMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Domain services
        services.AddScoped<ILoggingService, LoggingService>();

        // Application use cases
        services.AddScoped<ILoggingAppService, LoggingAppService>();

        return services;
    }
}
