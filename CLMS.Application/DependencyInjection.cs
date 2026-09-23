using CLMS.Application.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using CLMS.Application.Logging;
using CLMS.Application.Interfaces;
using CLMS.Application.Services;

namespace CLMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services
            .AddScoped<AuthenticationService>()
            .AddScoped<UserManagementService>()
            .AddScoped<LoginUseCase>()
            .AddScoped<ILoggingService, LoggingService>()
            .AddScoped<ILoggingAppService, LoggingAppService>()
            .AddScoped<ILogbookService, LogbookService>()  
            .AddScoped<IEquipmentService, EquipmentService>()
            .AddScoped<ISupplyService, SupplyService>()
            .AddScoped<ILaboratoryService, LaboratoryService>();
    }
}