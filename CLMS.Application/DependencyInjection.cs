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
            .AddScoped<CLMS.Application.UnitRegistry.UnitRegistryService>()
            .AddScoped<CLMS.Application.UnitRegistry.DeviceAuthenticationService>()
            .AddScoped<ComputerUnitService>()
            .AddScoped<AuthenticationService>()
            .AddScoped<EmailLoginService>()
            .AddScoped<TwoFactorSettingsService>()
            .AddScoped<PasswordRecoveryService>()
            .AddScoped<UserManagementService>()
            .AddScoped<LoggingService>()
            .AddScoped<LoggingAppService>()
            .AddScoped<LogbookService>()  
            .AddScoped<EquipmentService>()
            .AddScoped<SupplyService>()
            .AddScoped<LaboratoryService>()
            .AddScoped<ClassScheduleService>()
            .AddScoped<ClassListCsvParser>()
            .AddScoped<ClassListService>()
            .AddScoped<BookingService>()
            .AddScoped<UserService>()
            .AddScoped<CourseService>()
            .AddScoped<AcademicTermService>()
            .AddScoped<DashboardService>()
            ;
    }
}
