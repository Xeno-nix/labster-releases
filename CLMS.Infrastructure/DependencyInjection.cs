using Microsoft.Extensions.DependencyInjection;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Repositories;

namespace CLMS.Infrastructure;


public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IStudentRepository>(_ => new StudentRepository(connectionString));
        services.AddScoped<IGuestRepository>(_ => new GuestRepository(connectionString));
        services.AddScoped<ILabUtilizationRepository>(_ => new LabUtilizationRepository(connectionString));
        services.AddScoped<ILogbookRepository>(_ => new LogbookRepository(connectionString));

        services.AddScoped<IEquipmentRepository>(_ => new EquipmentRepository(connectionString));
        services.AddScoped<ISupplyRepository>(_ => new SupplyRepository(connectionString));
        services.AddScoped<ILaboratoryRepository>(_ => new LaboratoryRepository(connectionString));

        return services;
    }
}
