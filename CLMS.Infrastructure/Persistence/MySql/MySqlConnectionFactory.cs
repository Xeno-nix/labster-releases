using CLMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System.Data;

namespace CLMS.Infrastructure.Persistence.MySql;

public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ClmsDatabase")
            ?? throw new InvalidOperationException("Connection string 'ClmsDatabase' not found.");
    }

    public IDbConnection CreateConnection()
    {
        return new MySqlConnection(_connectionString);
    }
}
