using Microsoft.Extensions.DependencyInjection;
using CLMS.Application.Logging;
using CLMS.Application.Interfaces;
using CLMS.Application.Services;

namespace CLMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILoggingService, LoggingService>();
        services.AddScoped<ILoggingAppService, LoggingAppService>();
        
        services.AddScoped<ILogbookService, LogbookService>();

        return services;
    }
}
