using CLMS.Application.Authentication.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Infrastructure.Auth;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
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
            .AddScoped<IStudentRepository>(_ => new StudentRepository(connectionString))
            .AddScoped<IGuestRepository>(_ => new GuestRepository(connectionString))
            .AddScoped<ILabUtilizationRepository>(_ => new LabUtilizationRepository(connectionString))
            .AddScoped<ILogbookRepository>(_ => new LogbookRepository(connectionString
            .AddScoped<IEquipmentRepository>(_ => new EquipmentRepository(connectionString))
            .AddScoped<ISupplyRepository>(_ => new SupplyRepository(connectionString))
            .AddScoped<ILaboratoryRepository>(_ => new LaboratoryRepository(connectionString))
            .AddScoped<IDbConnectionFactory,MySqlConnectionFactory>();
        return services;
    }
}