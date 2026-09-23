using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System.Data.Common;

namespace CLMS.Infrastructure.Persistence.MySql;

public class MySqlConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        // NOTE : Change connection string base sa imong gamiton jud didto sa app settings or user secrets
        _connectionString = configuration.GetConnectionString("ClmsDatabase")
            ?? throw new InvalidOperationException("Connection string 'ClmsDatabase' not found.");
    }

    public MySqlConnection CreateConnection()
    {
        return new MySqlConnector.MySqlConnection(_connectionString);
    }
}
