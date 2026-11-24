using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

internal static class DatabaseObjects
{
    private static readonly string[] ObjectFiles = ["indexes.sql", "views.sql", "stored_functions.sql", "stored_procedures.sql", "triggers.sql"];

    // Handles the explicit mysql-client DELIMITER lines in the checked-in scripts.
    // Statement bodies (including strings and their internal semicolons) stay intact.
    internal static IEnumerable<string> Statements(string text)
    {
        var delimiter = ";";
        var buffer = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--") || trimmed.Length == 0) continue;
            if (trimmed.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
            {
                if (buffer.Length != 0) throw new InvalidOperationException("DELIMITER inside an unfinished statement.");
                delimiter = trimmed[10..].Trim();
                continue;
            }
            buffer.AppendLine(line);
            if (!trimmed.EndsWith(delimiter, StringComparison.Ordinal)) continue;
            var statement = buffer.ToString().TrimEnd();
            yield return statement[..^delimiter.Length];
            buffer.Clear();
        }
        if (buffer.Length != 0) throw new InvalidOperationException("Unterminated SQL statement.");
    }

    internal static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task Bootstrap(MySqlConnection connection, string root)
    {
        await Execute(connection, "SET FOREIGN_KEY_CHECKS=0");
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Database", "clms"), "*.sql").Order())
            {
                if (ObjectFiles.Contains(Path.GetFileName(file))) continue;
                foreach (var statement in Statements(await File.ReadAllTextAsync(file))) await Execute(connection, statement);
            }
        }
        finally { await Execute(connection, "SET FOREIGN_KEY_CHECKS=1"); }
        // This migration contains USE clms; discard only that database-selection statement.
        foreach (var statement in Statements(await File.ReadAllTextAsync(Path.Combine(root, "Database", "migrations", "2026-11-20-student-kiosk.sql"))))
        {
            if (statement.TrimStart().StartsWith("USE ", StringComparison.OrdinalIgnoreCase)) continue;
            await Execute(connection, statement);
        }
    }

    internal static async Task Install(MySqlConnection connection, string root)
    {
        if (!Version.TryParse(connection.ServerVersion.Split('-')[0], out var version) || version < new Version(8, 0, 21))
            throw new InvalidOperationException("These objects require MySQL 8.0.21 or later.");
        foreach (var file in ObjectFiles)
        {
            foreach (var original in Statements(await File.ReadAllTextAsync(Path.Combine(root, "Database", "clms", file))))
            {
                var sql = original;
                var index = Regex.Match(sql, @"^CREATE INDEX (\w+)\s+ON (\w+)", RegexOptions.IgnoreCase);
                if (index.Success)
                {
                    await using var check = new MySqlCommand("SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name=@table AND index_name=@name", connection);
                    check.Parameters.AddWithValue("table", index.Groups[2].Value);
                    check.Parameters.AddWithValue("name", index.Groups[1].Value);
                    if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0) continue;
                }
                try { await Execute(connection, sql); }
                catch (MySqlException ex) { throw new InvalidOperationException($"Installation failed in {file} (MySQL {ex.Number}): {ex.Message}", ex); }
            }
            Console.WriteLine($"Installed {file}");
        }
    }

    internal static async Task Check(MySqlConnection connection, string root)
    {
        foreach (Match index in Regex.Matches(await File.ReadAllTextAsync(Path.Combine(root, "Database", "clms", "indexes.sql")), @"CREATE INDEX (\w+)\s+ON (\w+)", RegexOptions.IgnoreCase))
        {
            await using var check = new MySqlCommand("SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name=@table AND index_name=@name", connection);
            check.Parameters.AddWithValue("table", index.Groups[2].Value);
            check.Parameters.AddWithValue("name", index.Groups[1].Value);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0) throw new InvalidOperationException($"Missing index: {index.Groups[1].Value}");
        }
        Console.WriteLine("PASS repository indexes");
        foreach (var (file, pattern, query) in new[]
        {
            ("views.sql", @"CREATE OR REPLACE VIEW (\w+)", "SELECT COUNT(*) FROM information_schema.views WHERE table_schema=DATABASE() AND table_name=@name"),
            ("stored_functions.sql", @"CREATE FUNCTION (\w+)", "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema=DATABASE() AND routine_name=@name AND routine_type='FUNCTION'"),
            ("stored_procedures.sql", @"CREATE PROCEDURE (\w+)", "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema=DATABASE() AND routine_name=@name AND routine_type='PROCEDURE'"),
            ("triggers.sql", @"CREATE TRIGGER (\w+)", "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema=DATABASE() AND trigger_name=@name")
        })
        {
            var matches = Regex.Matches(await File.ReadAllTextAsync(Path.Combine(root, "Database", "clms", file)), pattern);
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("name", name);
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new InvalidOperationException($"Missing database object: {name}");
                if (file == "views.sql") await Execute(connection, $"SELECT * FROM `{name}` LIMIT 0");
            }
            Console.WriteLine($"PASS {matches.Count} objects in {file}");
        }
    }
}
