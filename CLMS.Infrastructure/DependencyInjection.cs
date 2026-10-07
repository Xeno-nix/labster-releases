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
            .AddScoped<MySqlConnectionFactory>()
            
            .AddScoped<ITokenService, JwtAuthenticationService>()
            .AddScoped<IPasswordHasher,PasswordHasher>()
            .AddScoped<IRefreshTokenHasher,RefreshTokenHasher>()
            .AddScoped<IUserRepository,UserRepository>()
            .AddScoped<IRefreshTokenRepository,RefreshTokenRepository>() 
            .AddScoped<IRefreshTokenRepository,RefreshTokenRepository>()
            
            .AddScoped<IStudentRepository,StudentRepository>()
            .AddScoped<IGuestRepository,GuestRepository>()
            .AddScoped<ILabUtilizationRepository,LabUtilizationRepository>()
            .AddScoped<ILogbookRepository,LogbookRepository>()
            .AddScoped<IEquipmentRepository,EquipmentRepository>()
            .AddScoped<ISupplyRepository,SupplyRepository>()
            
            .AddScoped<IClassScheduleRepository,ClassScheduleRepository>()
            .AddScoped<IBookingRepository,BookingRepository>()
            .AddScoped<ILaboratoryRepository,LaboratoryRepository>()
            .AddScoped<ICourseRepository,CourseRepository>()
            .AddScoped<IAcademicTermRepository,AcademicTermRepository>()
            .AddScoped<IEmailService,EmailService>()
            ;
  
        return services;
    }
}