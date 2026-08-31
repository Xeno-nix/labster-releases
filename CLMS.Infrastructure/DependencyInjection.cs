using Microsoft.Extensions.DependencyInjection;
using CLMS.Domain.Repositories;
using CLMS.Domain.Common;
using CLMS.Infrastructure.Repositories;

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
        //
        // Registered as concrete singletons (not just their interfaces) so the SAME
        // instance is resolvable both as the Domain-facing interface (IStudentRepository
        // etc., used by LoggingService/LoggingAppService) and as the concrete type
        // (used only by CLMS.Web/Debug/TerminalDebugLogger for its GetAllForDebug()
        // dump — that's a TEMP-DEBUG concern, not a normal architectural pattern).
        // If you delete TerminalDebugLogger per its revert checklist, these can go back
        // to the simpler `AddSingleton<IStudentRepository, InMemoryStudentRepository>()`
        // form.
        services.AddSingleton<InMemoryStudentRepository>();
        services.AddSingleton<IStudentRepository>(sp => sp.GetRequiredService<InMemoryStudentRepository>());

        services.AddSingleton<InMemoryGuestRepository>();
        services.AddSingleton<IGuestRepository>(sp => sp.GetRequiredService<InMemoryGuestRepository>());

        services.AddSingleton<InMemoryLabUtilizationRepository>();
        services.AddSingleton<ILabUtilizationRepository>(sp => sp.GetRequiredService<InMemoryLabUtilizationRepository>());

        return services;
    }
}
