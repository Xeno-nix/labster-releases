using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Authentication.UseCases;
using CLMS.Application.Persistence.Interfaces;
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
            .AddScoped<LoginUseCase>()
            .AddScoped<LogoutUseCase>()
            .AddScoped<RefreshTokenUseCase>()
            .AddScoped<RegisterUseCase>()
            .AddScoped<ILoggingService, LoggingService>()
            .AddScoped<ILoggingAppService, LoggingAppService>()
            .AddScoped<ILogbookService, LogbookService>()  
            .AddScoped<IEquipmentService, EquipmentService>()
            .AddScoped<ISupplyService, SupplyService>()
            .AddScoped<ILaboratoryService, LaboratoryService>();
    }
}