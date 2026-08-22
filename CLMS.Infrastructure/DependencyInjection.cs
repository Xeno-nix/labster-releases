using CLMS.Application.Interfaces;
using CLMS.Infrastructure.Persistence.MySql;
using Microsoft.Extensions.DependencyInjection;

namespace CLMS.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<IDbConnectionFactory,
            MySqlConnectionFactory>();

        return services;
    }
}