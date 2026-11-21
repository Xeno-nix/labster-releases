using CLMS.Application.DTOs.Logbook;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class LogbookRepository(MySqlConnectionFactory connectionFactory) : ILogbookRepository
{
    public async Task<(List<LogbookEntryDTO> Entries, int TotalCount, bool SupportsLaboratory)> GetPagedAsync(LogbookQueryDTO query)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_logbook_get_paged(@search, @filter, @page, @size);", connection);
        command.Parameters.AddWithValue("search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("filter", (object?)query.Filter ?? DBNull.Value);
        command.Parameters.AddWithValue("page", query.Page);
        command.Parameters.AddWithValue("size", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Missing logbook count result.");
        var total = Convert.ToInt32(reader.GetValue(0));
        if (!await reader.NextResultAsync()) throw new InvalidOperationException("Missing logbook page result.");
        var entries = new List<LogbookEntryDTO>();
        while (await reader.ReadAsync()) entries.Add(MapEntry(reader));
        return (entries, total, true);
    }

    public async Task<List<LogbookEntryDTO>> GetAllFilteredAsync(LogbookQueryDTO query)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_logbook_get_all_filtered(@search, @filter);", connection);
        command.Parameters.AddWithValue("search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("filter", (object?)query.Filter ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var entries = new List<LogbookEntryDTO>();
        while (await reader.ReadAsync()) entries.Add(MapEntry(reader));
        return entries;
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
