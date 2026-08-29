using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Inventory;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Repositories;
using CLMS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CLMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IStudentRepository>(_ => new StudentRepository(connectionString));
        services.AddScoped<IGuestRepository>(_ => new GuestRepository(connectionString));
        services.AddScoped<ILabUtilizationRepository>(_ => new LabUtilizationRepository(connectionString));
        services.AddScoped<ILogbookRepository>(_ => new LogbookRepository(connectionString));

        services.AddScoped<IDbConnectionFactory, MySqlConnectionFactory>();

        // TEMP: the database schema isn't ready yet, so Inventory runs on an
        // in-memory store (seeded with sample data, resets on app restart).
        // Once the DB is ready, swap this line back to:
        //   services.AddScoped<IInventoryService, InventoryService>();
        services.AddSingleton<IInventoryService, InMemoryInventoryService>();

        return services;
    }
}
