using Microsoft.Extensions.DependencyInjection;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Repositories;

namespace CLMS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the logging module's data-access repositories against the real
    /// database, using the same connectionString-constructor pattern as the booking
    /// module's repositories (see Program.cs) for consistency — no EF Core/DbContext,
    /// no separate connection-factory abstraction; each repository opens its own
    /// MySqlConnection per call using MySqlConnector directly.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IStudentRepository>(_ => new StudentRepository(connectionString));
        services.AddScoped<IGuestRepository>(_ => new GuestRepository(connectionString));
        services.AddScoped<ILabUtilizationRepository>(_ => new LabUtilizationRepository(connectionString));
        services.AddScoped<ILogbookRepository>(_ => new LogbookRepository(connectionString));

        return services;
    }
}
