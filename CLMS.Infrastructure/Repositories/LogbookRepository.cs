using CLMS.Application.DTOs.Logbook;
using CLMS.Application.Interfaces.Repositories;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

/// <summary>
/// Port of LoggingService::buildLogQuery()/getLogs(). Queries `attendance_logs` LEFT
/// JOIN `students` / `guests` — see remarks on LogbookEntryDTO for why there's no
/// laboratory join/filter (attendance_logs has no lab_id column).
/// </summary>
public class LogbookRepository : ILogbookRepository
{
    private readonly string _connectionString;

    public LogbookRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<(List<LogbookEntryDTO> Entries, int TotalCount)> GetPagedAsync(LogbookQueryDTO query)
    {
        var (whereClause, parameters) = BuildWhereClause(query);
        var offset = Math.Max(0, (query.Page - 1) * query.PageSize);

        var dataSql = $"""
            SELECT
                al.utilization_log_id,
                al.student_id,
                s.first_name AS student_first_name,
                s.last_name AS student_last_name,
                g.guest_name,
                al.purpose_subject,
                al.log_date,
                al.time_in,
                al.time_out
            FROM attendance_logs al
            LEFT JOIN students s ON al.student_id = s.student_id
            LEFT JOIN guests g ON al.guest_id = g.guest_id
            {whereClause}
            ORDER BY al.time_in DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        var countSql = $"""
            SELECT COUNT(*)
            FROM attendance_logs al
            LEFT JOIN students s ON al.student_id = s.student_id
            LEFT JOIN guests g ON al.guest_id = g.guest_id
            {whereClause};
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        int totalCount;
        await using (var countCommand = new MySqlCommand(countSql, connection))
        {
            ApplyParameters(countCommand, parameters);
            totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        }

        var entries = new List<LogbookEntryDTO>();
        await using (var dataCommand = new MySqlCommand(dataSql, connection))
        {
            ApplyParameters(dataCommand, parameters);
            dataCommand.Parameters.AddWithValue("@PageSize", query.PageSize);
            dataCommand.Parameters.AddWithValue("@Offset", offset);

            await using var reader = await dataCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(MapEntry(reader));
            }
        }

        return (entries, totalCount);
    }

    public async Task<List<LogbookEntryDTO>> GetAllFilteredAsync(LogbookQueryDTO query)
    {
        var (whereClause, parameters) = BuildWhereClause(query);

        var sql = $"""
            SELECT
                al.utilization_log_id,
                al.student_id,
                s.first_name AS student_first_name,
                s.last_name AS student_last_name,
                g.guest_name,
                al.purpose_subject,
                al.log_date,
                al.time_in,
                al.time_out
            FROM attendance_logs al
            LEFT JOIN students s ON al.student_id = s.student_id
            LEFT JOIN guests g ON al.guest_id = g.guest_id
            {whereClause}
            ORDER BY al.time_in DESC;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        ApplyParameters(command, parameters);

        var entries = new List<LogbookEntryDTO>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapEntry(reader));
        }

        return entries;
    }

    // Builds the shared WHERE clause + parameter list used by both the count and data
    // queries (and the CSV export query) — mirrors buildLogQuery()'s conditional filters.
    private static (string WhereClause, List<(string Name, object Value)> Parameters) BuildWhereClause(LogbookQueryDTO query)
    {
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("""
                (al.purpose_subject LIKE @Search
                 OR s.student_id LIKE @Search
                 OR s.first_name LIKE @Search
                 OR s.last_name LIKE @Search
                 OR g.guest_name LIKE @Search)
                """);
            parameters.Add(("@Search", $"%{query.Search}%"));
        }

        switch (query.Filter)
        {
            case "day":
                conditions.Add("al.log_date = CURDATE()");
                break;
            case "week":
                // MySQL mode 1 = weeks start on Monday, matching Carbon's default startOfWeek().
                conditions.Add("YEARWEEK(al.log_date, 1) = YEARWEEK(CURDATE(), 1)");
                break;
            case "month":
                conditions.Add("MONTH(al.log_date) = MONTH(CURDATE()) AND YEAR(al.log_date) = YEAR(CURDATE())");
                break;
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        return (whereClause, parameters);
    }

    private static void ApplyParameters(MySqlCommand command, List<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
    }

    private static LogbookEntryDTO MapEntry(MySqlDataReader reader)
    {
        var studentIdOrdinal = reader.GetOrdinal("student_id");
        var firstNameOrdinal = reader.GetOrdinal("student_first_name");
        var lastNameOrdinal = reader.GetOrdinal("student_last_name");
        var guestNameOrdinal = reader.GetOrdinal("guest_name");
        var purposeOrdinal = reader.GetOrdinal("purpose_subject");
        var logDateOrdinal = reader.GetOrdinal("log_date");
        var timeInOrdinal = reader.GetOrdinal("time_in");
        var timeOutOrdinal = reader.GetOrdinal("time_out");

        var isStudent = !reader.IsDBNull(studentIdOrdinal);

        string name;
        if (isStudent)
        {
            var first = reader.IsDBNull(firstNameOrdinal) ? string.Empty : reader.GetString(firstNameOrdinal);
            var last = reader.IsDBNull(lastNameOrdinal) ? string.Empty : reader.GetString(lastNameOrdinal);
            name = $"{first} {last}".Trim();
        }
        else
        {
            name = reader.IsDBNull(guestNameOrdinal) ? "—" : reader.GetString(guestNameOrdinal);
        }

        return new LogbookEntryDTO
        {
            Id = reader.GetInt32("utilization_log_id"),
            StudentId = isStudent ? reader.GetString(studentIdOrdinal) : "GUEST",
            Name = name,
            Purpose = reader.IsDBNull(purposeOrdinal) ? null : reader.GetString(purposeOrdinal),
            Date = reader.IsDBNull(logDateOrdinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(logDateOrdinal)),
            TimeIn = reader.IsDBNull(timeInOrdinal) ? null : reader.GetDateTime(timeInOrdinal),
            TimeOut = reader.IsDBNull(timeOutOrdinal) ? null : reader.GetDateTime(timeOutOrdinal),
        };
    }
}
