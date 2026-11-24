using CLMS.Application.DTOs.Logbook;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class LogbookRepository(MySqlConnectionFactory connectionFactory) : ILogbookRepository
{
    public async Task<(List<LogbookEntryDTO> Entries, int TotalCount, bool SupportsLaboratory)> GetPagedAsync(LogbookQueryDTO query)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        // Count and page must see the same attendance snapshot.
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await using var command = new MySqlCommand(
            "CALL sp_logbook_get_paged(@Search,@Filter,@Page,@PageSize,@LaboratoryId)", connection, transaction);
        AddFilters(command, query);
        command.Parameters.AddWithValue("@Page", query.Page);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        var entries = new List<LogbookEntryDTO>();
        int count;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Logbook count result is missing.");
            count = checked((int)reader.GetInt64("total_count"));
            if (!await reader.NextResultAsync()) throw new InvalidOperationException("Logbook entries result is missing.");
            while (await reader.ReadAsync()) entries.Add(MapEntry(reader));
        }
        await transaction.CommitAsync();
        return (entries, count, true);
    }

    public async Task<List<LogbookEntryDTO>> GetAllFilteredAsync(LogbookQueryDTO query)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "CALL sp_logbook_get_all_filtered(@Search,@Filter,@LaboratoryId)", connection);
        AddFilters(command, query);
        var entries = new List<LogbookEntryDTO>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) entries.Add(MapEntry(reader));
        return entries;
    }

    private static void AddFilters(MySqlCommand command, LogbookQueryDTO query)
    {
        command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(query.Search) ? DBNull.Value : query.Search);
        command.Parameters.AddWithValue("@Filter", (object?)query.Filter ?? DBNull.Value);
        command.Parameters.AddWithValue("@LaboratoryId", query.LaboratoryId is > 0 ? query.LaboratoryId.Value : DBNull.Value);
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
        var laboratoryOrdinal = reader.GetOrdinal("lab_name");

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
            FacultyName = reader.IsDBNull(reader.GetOrdinal("faculty_name")) ? null : reader.GetString("faculty_name"),
            Course = reader.IsDBNull(reader.GetOrdinal("course")) ? null : reader.GetString("course"),
            Laboratory = reader.IsDBNull(laboratoryOrdinal) ? null : reader.GetString(laboratoryOrdinal),
            Date = reader.IsDBNull(logDateOrdinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(logDateOrdinal)),
            TimeIn = reader.IsDBNull(timeInOrdinal) ? null : reader.GetDateTime(timeInOrdinal),
            TimeOut = reader.IsDBNull(timeOutOrdinal) ? null : reader.GetDateTime(timeOutOrdinal),
        };
    }
}
