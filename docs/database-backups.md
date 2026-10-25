# Database backup and restore

Administrators can open **Backup & Restore** in the sidebar (`/admin/database-backups`).
Use **Back up now** for a manual snapshot. To restore a listed snapshot, stop agent and user writes,
select **Restore**, and type `RESTORE`. The selected file is checked against its SHA-256 metadata,
then a fresh recovery snapshot is made before any restore starts. No arbitrary SQL uploads are accepted.

The backup contains the configured database's schema and records, routines, events, and triggers.
It does not include MySQL users, server settings, application secrets, or uploaded files.
Restore replaces tables included in the snapshot; tables created after that snapshot are not removed.
MySQL DDL is not transactional: a failed restore can leave partial changes. The error identifies the
pre-restore snapshot so an administrator can restore it while writes remain stopped.

## Configuration

`DatabaseBackup` in `CLMS.Web/appsettings.json` controls:

- `Enabled`: enables the daily background job. Manual operations remain available.
- `HourPhilippineTime`: daily start hour in Philippine Standard Time (UTC+8), default `2` (02:00 PH time).
  Backup dates and restore confirmations also display PH time. Stored timestamps and filenames remain UTC.
- `KeepLast`: latest regular snapshots to retain, default `7`. Recovery snapshots have a separate
  allowance of the same size. Retention runs after successful regular backups only.
- `TimeoutMinutes`: maximum native-tool execution time, default `10`.
- `Directory`: private backup storage, resolved against the web content root. Default
  `App_Data/DatabaseBackups`, excluded from Git. Startup rejects storage inside `wwwroot`.
- `DumpExecutable` / `ClientExecutable`: native server-compatible `mysqldump` / `mysql` paths.
  Development configuration uses this machine's `C:/xampp/mysql/bin` tools. Production defaults
  use PATH; override for MariaDB tools or other installations. No new package is required.

The configured `DefaultConnection` supplies the TCP host, port, database, user, and password.
Credentials are passed to child processes via their environment, never command-line arguments.
Grant the server account private filesystem access to the backup directory. Backups contain private
database records; copy them to protected off-machine storage for recovery from disk loss.
The database account needs dump privileges (including routines/events) and restore DDL/DML privileges.
The built-in job runs only while the web server is running; missed runs are not replayed. Run one
web server scheduler per database. Operations are serialized within that server process.
Use InnoDB tables for consistent `--single-transaction` snapshots; avoid schema changes during a dump.

## Verification

Normal unit tests cover Philippine-time scheduling across UTC date boundaries. The optional native integration test creates its own
`clms_backup_test_<guid>` database, checks backup/restore, binary and Unicode data, recovery snapshots,
integrity rejection, path rejection, retention, and failed-tool cleanup, then drops only its test database.

PowerShell, with a local test database server:

```powershell
$env:CLMS_BACKUP_TEST_SERVER = 'Server=127.0.0.1;User ID=root;'
$env:CLMS_BACKUP_TEST_DUMP = 'C:/xampp/mysql/bin/mysqldump.exe'
$env:CLMS_BACKUP_TEST_CLIENT = 'C:/xampp/mysql/bin/mysql.exe'
dotnet test CLMS.Tests/CLMS.Tests.csproj
```

The integration test is skipped unless `CLMS_BACKUP_TEST_SERVER` is explicitly set.
