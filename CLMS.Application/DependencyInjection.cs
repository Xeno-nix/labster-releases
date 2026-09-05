using Microsoft.Extensions.DependencyInjection;
using CLMS.Application.Logging;
using CLMS.Application.Interfaces;
using CLMS.Application.Services;

namespace CLMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Logging: business rules/orchestration service, and the outer DTO-validation
        // use-case wrapper the Blazor terminal calls directly.
        services.AddScoped<ILoggingService, LoggingService>();
        services.AddScoped<ILoggingAppService, LoggingAppService>();

        // Admin logbook: read-only search/filter/pagination/CSV-export over attendance_logs.
        services.AddScoped<ILogbookService, LogbookService>();

        return services;
    }
}
