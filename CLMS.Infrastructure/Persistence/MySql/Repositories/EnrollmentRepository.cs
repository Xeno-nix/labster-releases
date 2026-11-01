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
        const string sql = """
            SELECT
                s.student_id,
                s.first_name,
                s.last_name,
                s.middle_name,
                s.suffix
            FROM student_enrollment se
            INNER JOIN students s
                ON s.student_id = se.student_id
            INNER JOIN class_schedule cs
                ON cs.class_schedule_id = se.class_schedule_id
            WHERE se.class_schedule_id = @ScheduleId
              AND cs.deleted_at IS NULL
            ORDER BY s.last_name, s.first_name;
            """;

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
        var ids = scheduleIds.ToList();
        if (ids.Count == 0)
            return [];

        var placeholders = string.Join(',', ids.Select((_, i) => $"@S{i}"));
        var sql = $"""
            SELECT DISTINCT se.student_id
            FROM student_enrollment se
            INNER JOIN class_schedule cs
                ON cs.class_schedule_id = se.class_schedule_id
            WHERE cs.deleted_at IS NULL
              AND se.class_schedule_id IN ({placeholders});
            """;

        var result = new List<string>();

        var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
        await using (ownedConnection)
        await using (command)
        {
            for (var i = 0; i < ids.Count; i++)
                command.Parameters.AddWithValue($"@S{i}", ids[i]);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(reader.GetString("student_id"));
            }
        }

        return result;
    }

    public async Task<int> EnrollAsync(
        IEnumerable<int> scheduleIds,
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var slots = scheduleIds.ToList();
        var students = studentIds.Distinct().ToList();
        if (slots.Count == 0 || students.Count == 0)
            return 0;

        // Cross product: every student in every slot. Rows that already exist are left
        // untouched by ON DUPLICATE KEY UPDATE (assigning enrollment_id to itself, so the
        // affected-rows count still reports genuinely new rows).
        var rows = new List<(string StudentId, int ScheduleId)>(slots.Count * students.Count);
        foreach (var slot in slots)
        foreach (var student in students)
            rows.Add((student, slot));

        var totalInserted = 0;
        const int maxRowsPerBatch = 500;

        for (var offset = 0; offset < rows.Count; offset += maxRowsPerBatch)
        {
            var batch = rows.Skip(offset).Take(maxRowsPerBatch).ToList();

            var valueClauses = batch
                .Select((_, i) => $"(@Student{i}, @Schedule{i})");

            var sql = $"""
                INSERT INTO student_enrollment (student_id, class_schedule_id)
                VALUES {string.Join(',', valueClauses)}
                ON DUPLICATE KEY UPDATE enrollment_id = enrollment_id;
                """;

            var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                for (var i = 0; i < batch.Count; i++)
                {
                    command.Parameters.AddWithValue($"@Student{i}", batch[i].StudentId);
                    command.Parameters.AddWithValue($"@Schedule{i}", batch[i].ScheduleId);
                }

                var inserted = await command.ExecuteNonQueryAsync();
                totalInserted += inserted;
            }
        }

        return totalInserted;
    }

    public async Task<int> RemoveAsync(
        IEnumerable<int> scheduleIds,
        string studentId,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var slots = scheduleIds.ToList();
        if (slots.Count == 0)
            return 0;

        var placeholders = string.Join(',', slots.Select((_, i) => $"@S{i}"));
        var sql = $"""
            DELETE FROM student_enrollment
            WHERE student_id = @StudentId
              AND class_schedule_id IN ({placeholders});
            """;

        var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
        await using (ownedConnection)
        await using (command)
        {
            command.Parameters.AddWithValue("@StudentId", studentId);
            for (var i = 0; i < slots.Count; i++)
                command.Parameters.AddWithValue($"@S{i}", slots[i]);

            return await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<int> DeleteAllAsync(
        IEnumerable<int> scheduleIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var slots = scheduleIds.ToList();
        if (slots.Count == 0)
            return 0;

        var placeholders = string.Join(',', slots.Select((_, i) => $"@S{i}"));
        var sql = $"""
            DELETE FROM student_enrollment
            WHERE class_schedule_id IN ({placeholders});
            """;

        var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
        await using (ownedConnection)
        await using (command)
        {
            for (var i = 0; i < slots.Count; i++)
                command.Parameters.AddWithValue($"@S{i}", slots[i]);

            return await command.ExecuteNonQueryAsync();
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
        var ids = scheduleIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, int>();

        var result = new Dictionary<int, int>();
        const int maxIdsPerBatch = 500;

        for (var offset = 0; offset < ids.Count; offset += maxIdsPerBatch)
        {
            var batch = ids.Skip(offset).Take(maxIdsPerBatch).ToList();
            var placeholders = string.Join(',', batch.Select((_, i) => $"@S{i}"));

            var sql = $"""
                SELECT se.class_schedule_id, COUNT(DISTINCT se.student_id) AS cnt
                FROM student_enrollment se
                INNER JOIN class_schedule cs
                    ON cs.class_schedule_id = se.class_schedule_id
                WHERE cs.deleted_at IS NULL
                  AND se.class_schedule_id IN ({placeholders})
                GROUP BY se.class_schedule_id;
                """;

            var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                for (var i = 0; i < batch.Count; i++)
                    command.Parameters.AddWithValue($"@S{i}", batch[i]);

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    result[reader.GetInt32("class_schedule_id")] = reader.GetInt32("cnt");
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