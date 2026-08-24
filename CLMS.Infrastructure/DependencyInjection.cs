using Microsoft.Extensions.DependencyInjection;
using CLMS.Domain.Repositories;
using CLMS.Domain.Common;
using CLMS.Infrastructure.Repositories;
using CLMS.Infrastructure.Security;

namespace CLMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // TEMPORARY: in-memory repository implementations. There's no real database
        // wired up yet, so these hold data only for the lifetime of the app process
        // (hence AddSingleton, not AddScoped) — data is lost on restart and isn't
        // shared across app instances. Replace with real data-access implementations
        // (EF Core or otherwise) once the database is ready; at that point these should
        // go back to AddScoped alongside a DbContext registration.
        services.AddSingleton<IStudentRepository, InMemoryStudentRepository>();
        services.AddSingleton<IGuestRepository, InMemoryGuestRepository>();
        services.AddSingleton<ILabUtilizationRepository, InMemoryLabUtilizationRepository>();

        // Password hasher for user credentials
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        return services;
    }
}
