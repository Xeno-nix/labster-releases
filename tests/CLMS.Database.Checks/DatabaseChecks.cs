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
        await using var periods = new MySqlCommand("""
            SELECT sf_logbook_period_start('week','2027-01-01')='2026-12-28'
              AND sf_logbook_period_start('month','2028-02-29')='2028-02-01'
              AND sf_logbook_period_start('day','2028-02-29')='2028-02-29'
              AND sf_logbook_period_start('all','2028-02-29') IS NULL
              AND sf_logbook_period_start('day',NULL) IS NULL
            """, connection);
        Require(Convert.ToBoolean(await periods.ExecuteScalarAsync()), "period function: New Year, leap day, all and null");
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build());
        await RepositoryChecks.Run(connection, factory);
        var attendance = await Scalar(connection, "SELECT COUNT(*) FROM attendance_logs");
        await DatabaseObjects.Install(connection, root);
        Require(await Scalar(connection, "SELECT COUNT(*) FROM attendance_logs") == attendance, "reinstallation preserves attendance");
        await DatabaseObjects.Check(connection, root);
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
