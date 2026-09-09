using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System.Data.Common;

namespace CLMS.Infrastructure.Persistence.MySql;

public class MySqlConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ClmsDatabase")
            ?? throw new InvalidOperationException("Connection string 'ClmsDatabase' not found.");
    }

    public MySqlConnection CreateConnection()
    {
        return new MySqlConnector.MySqlConnection(_connectionString);
    }
}
