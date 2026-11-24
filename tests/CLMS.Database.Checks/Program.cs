using Microsoft.Extensions.Configuration;
using MySqlConnector;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (!File.Exists(Path.Combine(root.FullName, "CLMS.slnx")))
    root = root.Parent ?? throw new InvalidOperationException("Run from the CLMS checkout.");
if (args.Length != 0)
{
    Console.Error.WriteLine("This project runs disposable database checks only; installation commands are not supported.");
    return 2;
}
var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root.FullName, "CLMS.Web"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "UserSecrets", "9943bc97-70c5-4636-9266-e1ead8eca095", "secrets.json"), optional: true)
    .AddEnvironmentVariables().Build();
var settings = new MySqlConnectionStringBuilder(
    Environment.GetEnvironmentVariable("CLMS_TEST_MYSQL") ?? config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured."))
    { AllowUserVariables = true, ConnectionTimeout = 10, DefaultCommandTimeout = 120 };
try
{
    {
        var database = "clms_objects_test_" + Guid.NewGuid().ToString("N");
        settings.Database = "";
        await using var admin = new MySqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        await DatabaseObjects.Execute(admin, $"CREATE DATABASE `{database}`");
        try
        {
            settings.Database = database;
            await using var test = new MySqlConnection(settings.ConnectionString);
            await test.OpenAsync();
            await DatabaseObjects.Bootstrap(test, root.FullName);
            await DatabaseObjects.Install(test, root.FullName);
            // Reinstallation must preserve data and work with existing indexes.
            await DatabaseObjects.Install(test, root.FullName);
            await DatabaseChecks.Run(test, root.FullName, settings.ConnectionString);
        }
        finally
        {
            // Only this generated database is ever dropped; configured databases are never dropped.
            await DatabaseObjects.Execute(admin, $"DROP DATABASE `{database}`");
        }
        Console.WriteLine("PASS disposable database installation and integration checks");
    }
    return 0;
}
catch (MySqlException ex)
{
    // Connection strings, passwords, and row data must never be printed by this tool.
    Console.Error.WriteLine($"MySQL error {ex.Number}: {ex.Message}");
    return 1;
}
catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
