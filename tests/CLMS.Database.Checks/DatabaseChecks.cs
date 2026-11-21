using MySqlConnector;
using CLMS.Infrastructure.Persistence.MySql;
using Microsoft.Extensions.Configuration;

internal static class DatabaseChecks
{
    internal static async Task Run(MySqlConnection connection, string root, string connectionString)
    {
        await DatabaseObjects.Check(connection, root);
        await using var command = new MySqlCommand("SELECT sf_registration_effective_status('Pending', '2026-01-01', '2026-01-02')", connection);
        if (!Equals(await command.ExecuteScalarAsync(), "Expired")) throw new Exception("Effective status function failed.");
        Console.WriteLine("PASS effective registration status");
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build());
        await RepositoryChecks.Run(connection, factory);
        await DatabaseObjects.Install(connection, root);
    }

    internal static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        Console.WriteLine("PASS " + name);
    }

    internal static async Task<long> Scalar(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
