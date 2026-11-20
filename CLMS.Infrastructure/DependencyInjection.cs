using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Infrastructure.Auth;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Infrastructure.Repositories;
using CLMS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CLMS.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IAccountSecurityCrypto, AccountSecurityCrypto>()
            .AddSingleton<IResetTokenHasher>(sp => sp.GetRequiredService<IAccountSecurityCrypto>())
            .AddSingleton<IEmailLoginChallenges, EmailLoginChallenges>()
            .AddSingleton<IPasswordRecoveryQueue, PasswordRecoveryQueue>()
            .AddScoped<IResetTokenRepository, ResetTokenRepository>()
            .AddScoped<CLMS.Application.UnitRegistry.IUnitRegistryRepository, UnitRegistryRepository>()
            .AddScoped<CLMS.Application.UnitRegistry.IAgentTokenService, AgentTokenService>()
            .AddSingleton<CLMS.Application.UnitRegistry.IDeviceProofVerifier, DeviceProofVerifier>()
            .AddScoped<MySqlConnectionFactory>()
            .AddScoped<CLMS.Application.Monitoring.IApplicationRegistryRepository, ApplicationRegistryRepository>()
            .AddScoped<CLMS.Application.Monitoring.IAbnormalActivityRepository, AbnormalActivityRepository>()
            .AddScoped<WorkstationSessionRepository>()
            
            .AddScoped<ITokenService, JwtAuthenticationService>()
            .AddScoped<IPasswordHasher,PasswordHasher>()
            .AddScoped<IRefreshTokenHasher,RefreshTokenHasher>()
            .AddScoped<IUserRepository,UserRepository>()
            .AddScoped<IRefreshTokenRepository,RefreshTokenRepository>() 
            .AddScoped<IRefreshTokenRepository,RefreshTokenRepository>()
            .AddScoped<IExternalIdentityRepository, ExternalIdentityRepository>()
            
            .AddScoped<IDbConnectionFactory, DbConnectionFactoryAdapter>()
            .AddScoped<IEnrollmentRepository, EnrollmentRepository>()
            .AddScoped<IStudentRepository,StudentRepository>()
            .AddScoped<IGuestRepository,GuestRepository>()
            .AddScoped<ILabUtilizationRepository,LabUtilizationRepository>()
            .AddScoped<ILogbookRepository,LogbookRepository>()
            .AddScoped<IEquipmentRepository,EquipmentRepository>()
            .AddScoped<ISupplyRepository,SupplyRepository>()
            .AddScoped<IComputerUnitRepository, ComputerUnitRepository>()
            .AddScoped<IUnitMonitoringRepository, UnitMonitoringRepository>()
            .AddScoped<IScreenCastRepository, ScreenCastRepository>()
            
            .AddScoped<IClassScheduleRepository,ClassScheduleRepository>()
            .AddScoped<IBookingRepository,BookingRepository>()
            .AddScoped<ILaboratoryRepository,LaboratoryRepository>()
            .AddScoped<ICourseRepository,CourseRepository>()
            .AddScoped<IAcademicTermRepository,AcademicTermRepository>()
            ;
        
        services.AddHttpClient<IEmailService, EmailService>(client => client.Timeout = TimeSpan.FromSeconds(20));
  
        return services;
    }
}
