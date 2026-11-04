namespace CLMS.Infrastructure.DatabaseMaintenance;

public sealed class DatabaseBackupOptions
{
    public bool Enabled { get; set; } = true;
    public int HourPhilippineTime { get; set; } = 2;
    public int KeepLast { get; set; } = 7;
    public int TimeoutMinutes { get; set; } = 10;
    public string Directory { get; set; } = "App_Data/DatabaseBackups";
    public string DumpExecutable { get; set; } = "mysqldump";
    public string ClientExecutable { get; set; } = "mysql";

    public DateTimeOffset NextRun(DateTimeOffset now)
    {
        var offset = TimeSpan.FromHours(8);
        var next = new DateTimeOffset(now.ToOffset(offset).Date.AddHours(HourPhilippineTime), offset);
        return (next > now ? next : next.AddDays(1)).ToUniversalTime();
    }
}

public sealed record DatabaseBackup(string FileName, DateTimeOffset CreatedUtc, long Bytes,
    string Database, string Sha256, string Kind);
