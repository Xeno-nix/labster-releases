using System.Text.Json;
using System.Data.Common;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class EnrollmentRepository(
    MySqlConnectionFactory connectionFactory) : IEnrollmentRepository
{
    public async Task<IReadOnlyList<EnrolledStudentDTO>> GetRosterAsync(int scheduleId)
    {
        const string sql = "CALL sp_enrollment_get_roster(@ScheduleId);";

        var roster = new List<EnrolledStudentDTO>();

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@ScheduleId", scheduleId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            roster.Add(MapEnrolledStudent(reader));
        }

        return roster;
    }

    public async Task<IReadOnlyList<string>> GetEnrolledStudentIdsAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var ids = scheduleIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var (command, ownedConnection) = await CreateCommandAsync("CALL sp_enrollment_get_enrolled_student_ids(@ids);", connection, transaction);
        await using (ownedConnection)
        await using (command)
        {
            command.Parameters.AddWithValue("ids", JsonSerializer.Serialize(ids));
            var result = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(reader.GetString("student_id"));
            return result;
        }
    }

    public async Task<int> EnrollAsync(
        IEnumerable<int> scheduleIds,
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var slots = scheduleIds.Distinct().ToArray();
        var students = studentIds.Distinct().ToArray();
        var inserted = 0;
        // Bound each cross-product insert to 500 rows and retain the caller's transaction.
        foreach (var slot in slots)
        foreach (var batch in students.Chunk(500))
        {
            var (command, ownedConnection) = await CreateCommandAsync("CALL sp_enrollment_enroll(@slots, @students);", connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                command.Parameters.AddWithValue("slots", JsonSerializer.Serialize(new[] { slot }));
                command.Parameters.AddWithValue("students", JsonSerializer.Serialize(batch));
                inserted += await command.ExecuteAffectedRowsAsync();
            }
        }
        return inserted;
    }

    public async Task<int> RemoveAsync(
        IEnumerable<int> scheduleIds,
        string studentId,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var slots = scheduleIds.Distinct().ToArray();
        if (slots.Length == 0) return 0;
        var (command, ownedConnection) = await CreateCommandAsync("CALL sp_enrollment_remove(@slots, @student);", connection, transaction);
        await using (ownedConnection)
        await using (command)
        {
            command.Parameters.AddWithValue("slots", JsonSerializer.Serialize(slots));
            command.Parameters.AddWithValue("student", studentId);
            return await command.ExecuteAffectedRowsAsync();
        }
    }

    public async Task<int> DeleteAllAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var slots = scheduleIds.Distinct().ToArray();
        if (slots.Length == 0) return 0;
        var (command, ownedConnection) = await CreateCommandAsync("CALL sp_enrollment_delete_all(@slots);", connection, transaction);
        await using (ownedConnection)
        await using (command)
        {
            command.Parameters.AddWithValue("slots", JsonSerializer.Serialize(slots));
            return await command.ExecuteAffectedRowsAsync();
        }
    }

    /// <summary>
    /// Distinct student count per slot, used by the sync status. Counts only enrollments
    /// on non-deleted schedules so a soft-deleted meeting doesn't appear "missing".
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> GetCountsAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var result = new Dictionary<int, int>();
        foreach (var batch in scheduleIds.Distinct().Chunk(500))
        {
            var (command, ownedConnection) = await CreateCommandAsync("CALL sp_enrollment_get_counts(@ids);", connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                command.Parameters.AddWithValue("ids", JsonSerializer.Serialize(batch));
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) result[reader.GetInt32("class_schedule_id")] = reader.GetInt32("cnt");
            }
        }
        return result;
    }

    private static EnrolledStudentDTO MapEnrolledStudent(MySqlDataReader reader)
    {
        var firstNameOrdinal = reader.GetOrdinal("first_name");
        var lastNameOrdinal = reader.GetOrdinal("last_name");
        var middleNameOrdinal = reader.GetOrdinal("middle_name");
        var suffixOrdinal = reader.GetOrdinal("suffix");

        return new EnrolledStudentDTO
        {
            StudentId = reader.GetString("student_id"),
            FirstName = reader.IsDBNull(firstNameOrdinal) ? null : reader.GetString(firstNameOrdinal),
            LastName = reader.IsDBNull(lastNameOrdinal) ? null : reader.GetString(lastNameOrdinal),
            MiddleName = reader.IsDBNull(middleNameOrdinal) ? null : reader.GetString(middleNameOrdinal),
            Suffix = reader.IsDBNull(suffixOrdinal) ? null : reader.GetString(suffixOrdinal)
        };
    }

    /// <summary>
    /// Builds a command on the caller's connection when one was supplied, otherwise opens a
    /// new one that the caller must dispose (returned as <c>ownedConnection</c>).
    /// </summary>
    private async Task<(MySqlCommand Command, MySqlConnection? OwnedConnection)> CreateCommandAsync(
        string sql,
        DbConnection? connection,
        DbTransaction? transaction)
    {
        MySqlConnection conn;
        MySqlConnection? ownedConnection = null;

        if (connection is MySqlConnection provided)
        {
            conn = provided;
        }
        else
        {
            conn = connectionFactory.CreateConnection();
            ownedConnection = conn;
            await conn.OpenAsync();
        }

        var command = new MySqlCommand(sql, conn);

        if (transaction is MySqlTransaction tx)
            command.Transaction = tx;

        return (command, ownedConnection);
    }
}