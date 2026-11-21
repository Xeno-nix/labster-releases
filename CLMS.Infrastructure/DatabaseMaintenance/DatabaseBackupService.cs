using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace CLMS.Infrastructure.DatabaseMaintenance;

public sealed class DatabaseBackupService
{
    private readonly DatabaseBackupOptions options;
    private readonly MySqlConnectionStringBuilder connection;
    private readonly ILogger<DatabaseBackupService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    public string BackupDirectory { get; }

    public DatabaseBackupService(IConfiguration configuration, IOptions<DatabaseBackupOptions> settings,
        ILogger<DatabaseBackupService> logger)
    {
        options = settings.Value;
        this.logger = logger;
        connection = new(configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection is not configured."));
        if (string.IsNullOrWhiteSpace(connection.Database))
            throw new InvalidOperationException("A database name is required for backups.");
        BackupDirectory = Path.GetFullPath(options.Directory);
    }

    public IReadOnlyList<DatabaseBackup> List()
    {
        if (!Directory.Exists(BackupDirectory)) return [];
        var backups = new List<DatabaseBackup>();
        foreach (var path in Directory.EnumerateFiles(BackupDirectory, "clms-*.sql.json"))
        {
            try
            {
                var backup = JsonSerializer.Deserialize<DatabaseBackup>(File.ReadAllText(path));
                if (backup is not null && backup.Database == connection.Database &&
                    Path.GetFileName(path) == backup.FileName + ".json" &&
                    File.Exists(Resolve(backup.FileName)))
                    backups.Add(backup);
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
            {
                logger.LogWarning("Ignoring unreadable backup metadata: {File}", Path.GetFileName(path));
            }
        }
        return backups.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    public async Task<DatabaseBackup> CreateAsync(string kind = "manual", CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A backup or restore is already running.");
        try
        {
            var backup = await CreateCoreAsync(kind, cancellationToken);
            Prune();
            return backup;
        }
        finally { gate.Release(); }
    }

    public Task<DatabaseBackup> RestoreAsync(string fileName, CancellationToken cancellationToken = default) => RestoreAsync(fileName, null, cancellationToken);

    public async Task<DatabaseBackup> RestoreAsync(string fileName, Func<Task>? authorize, CancellationToken cancellationToken = default)
    {
        var path = Resolve(fileName);
        if (!await gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A backup or restore is already running.");
        try
        {
            var backup = List().FirstOrDefault(b => b.FileName == fileName)
                ?? throw new InvalidOperationException("The selected backup is unavailable.");
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
            if (source.Length != backup.Bytes || hash != backup.Sha256)
                throw new InvalidOperationException("Backup integrity check failed. Restore was not started.");
            source.Position = 0;
            await ValidateRestoreAsync(source, cancellationToken);
            source.Position = 0;
            var safety = await CreateCoreAsync("pre-restore", cancellationToken);
            logger.LogWarning("Restoring database {Database} from {File}. Recovery backup: {Safety}",
                connection.Database, fileName, safety.FileName);
            try
            {
                if (authorize is not null) await authorize();
                await RunAsync(options.ClientExecutable, ["--binary-mode", "--default-character-set=utf8mb4"],
                    source, null, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Restore did not complete; the database may be partially restored. Recovery backup: {safety.FileName}. Stop database writes and retry a restore.", ex);
            }
            logger.LogInformation("Database restore completed: {File}", fileName);
            return safety;
        }
        finally { gate.Release(); }
    }

    private async Task ValidateRestoreAsync(Stream source, CancellationToken ct)
    {
        var database = "clms_restore_validation_" + Guid.NewGuid().ToString("N");
        var server = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = "" };
        await using var admin = new MySqlConnection(server.ConnectionString);
        await admin.OpenAsync(ct);
        await using var create = new MySqlCommand($"CREATE DATABASE `{database}`", admin);
        await create.ExecuteNonQueryAsync(ct);
        try
        {
            await RunAsync(options.ClientExecutable, ["--binary-mode", "--default-character-set=utf8mb4"], source, null, ct, database);
            server.Database = database;
            await using var validation = new MySqlConnection(server.ConnectionString); await validation.OpenAsync(ct);
            await using var schema = new MySqlCommand("""
                SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=@database
                AND TABLE_NAME IN ('authorization_roles','role_privileges','user_privilege_overrides','authorization_audit')
                """, validation);
            schema.Parameters.AddWithValue("@database", database);
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(ct)) != 4)
                throw new InvalidOperationException("This backup predates the authorization schema. Use an operator-led offline recovery instead.");
            await using var protectedRole = new MySqlCommand("SELECT COUNT(*) FROM authorization_roles WHERE role_id=2 AND name='SuperAdmin' AND is_protected=TRUE", validation);
            if (Convert.ToInt32(await protectedRole.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("Restore rejected: the protected authorization role is missing.");
            await using var roots = new MySqlCommand("""
                SELECT COUNT(*) FROM users WHERE role_type='superadmin' AND deleted_at IS NULL
                    AND password_changed=TRUE AND password_hash IS NOT NULL AND password_hash<>''
                    AND (lockout_end IS NULL OR lockout_end<=UTC_TIMESTAMP())
                """, validation);
            if (Convert.ToInt32(await roots.ExecuteScalarAsync(ct)) == 0)
                throw new InvalidOperationException("Restore rejected: the backup has no usable SuperAdmin account.");
        }
        finally
        {
            // The name is generated here, never supplied by a browser or backup filename.
            if (database.StartsWith("clms_restore_validation_", StringComparison.Ordinal) && database.Length == 56)
            {
                await using var drop = new MySqlCommand($"DROP DATABASE `{database}`", admin);
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    private async Task<DatabaseBackup> CreateCoreAsync(string kind, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BackupDirectory);
        var fileName = $"clms-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.sql";
        var path = Resolve(fileName);
        var temporary = path + ".partial";
        try
        {
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await RunAsync(options.DumpExecutable,
                    ["--single-transaction", "--quick", "--routines", "--events", "--triggers", "--hex-blob",
                     "--no-tablespaces", "--skip-add-locks", "--default-character-set=utf8mb4"],
                    null, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
            if (new FileInfo(temporary).Length == 0)
                throw new InvalidOperationException("The database tool produced an empty backup.");
            await using var contents = File.OpenRead(temporary);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(contents, cancellationToken));
            var backup = new DatabaseBackup(fileName, DateTimeOffset.UtcNow, contents.Length, connection.Database, hash, kind);
            contents.Close();
            File.Move(temporary, path);
            await File.WriteAllTextAsync(path + ".json", JsonSerializer.Serialize(backup), cancellationToken);
            logger.LogInformation("Database backup completed: {File} ({Bytes} bytes)", fileName, backup.Bytes);
            return backup;
        }
        catch
        {
            File.Delete(temporary);
            File.Delete(path);
            File.Delete(path + ".json");
            throw;
        }
    }

    private string Resolve(string fileName)
    {
        if (!Regex.IsMatch(fileName, @"\Aclms-\d{8}-\d{6}-[a-f0-9]{32}\.sql\z"))
            throw new ArgumentException("Invalid backup file name.");
        var path = Path.Combine(BackupDirectory, fileName);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Backup files cannot be symbolic links.");
        return path;
    }

    private void Prune()
    {
        // Keep recovery snapshots separately so routine retention cannot remove the last pre-restore copy.
        foreach (var group in List().GroupBy(b => b.Kind == "pre-restore"))
        foreach (var backup in group.Skip(options.KeepLast))
        {
            try
            {
                File.Delete(Resolve(backup.FileName));
                File.Delete(Resolve(backup.FileName) + ".json");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning(ex, "Could not remove expired backup {File}", backup.FileName); }
        }
    }

    private async Task RunAsync(string executable, string[] arguments, Stream? input, Stream? output,
        CancellationToken cancellationToken, string? targetDatabase = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
        var token = timeout.Token;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--no-defaults");
        start.ArgumentList.Add("--protocol=TCP");
        start.ArgumentList.Add($"--host={connection.Server}");
        start.ArgumentList.Add($"--port={connection.Port}");
        start.ArgumentList.Add($"--user={connection.UserID}");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(targetDatabase ?? connection.Database);
        // Keep the password out of process arguments and logs.
        start.Environment["MYSQL_PWD"] = connection.Password;
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Could not start the database tool.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Database tool unavailable. Check DatabaseBackup executable paths.");
        }
        using var stop = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(token);
        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(output ?? Stream.Null, token);
        async Task CopyInputAsync()
        {
            try { if (input is not null) await input.CopyToAsync(process.StandardInput.BaseStream, token); }
            finally { process.StandardInput.Close(); }
        }
        await Task.WhenAll(CopyInputAsync(), copyOutput, process.WaitForExitAsync(token), errors);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Database tool failed (exit {process.ExitCode}). Check database access and client/server compatibility.");
    }
}
