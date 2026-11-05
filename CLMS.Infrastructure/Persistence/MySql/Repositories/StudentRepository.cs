using System.Data.Common;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class StudentRepository(MySqlConnectionFactory connectionFactory) : IStudentRepository
{
    public async Task<Student?> GetByIdAsync(string studentId)
    {
        const string sql = """
            SELECT
                student_id,
                first_name,
                last_name,
                middle_name,
                suffix
            FROM students
            WHERE student_id = @StudentId;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@StudentId", studentId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapStudent(reader);
    }

    /// <summary>
    /// NULL-safe display lookup: returns a student's id and whatever name parts are present
    /// without building a Name value object, so NULLs and names the Name VO rejects are fine.
    /// </summary>
    public async Task<EnrolledStudentDTO?> FindByIdAsync(string studentId)
    {
        const string sql = """
            SELECT
                student_id,
                first_name,
                last_name,
                middle_name,
                suffix
            FROM students
            WHERE student_id = @StudentId;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@StudentId", studentId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapRosterStudent(reader);
    }

    public async Task<IReadOnlyList<string>> GetExistingIdsAsync(
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var result = new List<string>();
        const int maxIdsPerBatch = 500;

        for (var offset = 0; offset < ids.Count; offset += maxIdsPerBatch)
        {
            var batch = ids.Skip(offset).Take(maxIdsPerBatch).ToList();
            var placeholders = string.Join(',', batch.Select((_, i) => $"@Id{i}"));

            var sql = $"""
                SELECT student_id
                FROM students
                WHERE student_id IN ({placeholders});
                """;

            var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                for (var i = 0; i < batch.Count; i++)
                    command.Parameters.AddWithValue($"@Id{i}", batch[i]);

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    result.Add(reader.GetString("student_id"));
            }
        }

        return result;
    }

    /// <summary>
    /// NULL-safe batch lookup mirroring <see cref="FindByIdAsync"/>, for the CSV import
    /// preview's "name differs" note. Uses the same mapping as the roster so NULL name parts
    /// and names the Name VO rejects are handled without throwing.
    /// </summary>
    public async Task<IReadOnlyList<EnrolledStudentDTO>> FindByIdsAsync(
        IEnumerable<string> studentIds,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var result = new List<EnrolledStudentDTO>();
        const int maxIdsPerBatch = 500;

        for (var offset = 0; offset < ids.Count; offset += maxIdsPerBatch)
        {
            var batch = ids.Skip(offset).Take(maxIdsPerBatch).ToList();
            var placeholders = string.Join(',', batch.Select((_, i) => $"@Id{i}"));

            var sql = $"""
                SELECT student_id, first_name, last_name, middle_name, suffix
                FROM students
                WHERE student_id IN ({placeholders});
                """;

            var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                for (var i = 0; i < batch.Count; i++)
                    command.Parameters.AddWithValue($"@Id{i}", batch[i]);

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    result.Add(MapRosterStudent(reader));
            }
        }

        return result;
    }

    public async Task CreateManyAsync(
        IEnumerable<Student> students,
        DbConnection? connection = null,
        DbTransaction? transaction = null)
    {
        var rows = students.ToList();
        if (rows.Count == 0)
            return;

        const int maxRowsPerBatch = 200;

        for (var offset = 0; offset < rows.Count; offset += maxRowsPerBatch)
        {
            var batch = rows.Skip(offset).Take(maxRowsPerBatch).ToList();
            var valueClauses = batch.Select((_, i) => $"(@Student{i}, @First{i}, @Last{i}, @Middle{i}, @Suffix{i})");

            var sql = $"""
                INSERT INTO students (student_id, first_name, last_name, middle_name, suffix)
                VALUES {string.Join(',', valueClauses)}
                ON DUPLICATE KEY UPDATE student_id = student_id;
                """;

            var (command, ownedConnection) = await CreateCommandAsync(sql, connection, transaction);
            await using (ownedConnection)
            await using (command)
            {
                for (var i = 0; i < batch.Count; i++)
                {
                    var student = batch[i];
                    command.Parameters.AddWithValue($"@Student{i}", student.StudentId);
                    command.Parameters.AddWithValue($"@First{i}", student.FirstName.Value);
                    command.Parameters.AddWithValue($"@Last{i}", student.LastName.Value);
                    command.Parameters.AddWithValue($"@Middle{i}", (object?)student.MiddleName?.Value ?? DBNull.Value);
                    command.Parameters.AddWithValue($"@Suffix{i}", (object?)student.Suffix ?? DBNull.Value);
                }

                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static Student MapStudent(MySqlDataReader reader)
    {
        // NOTE: CLMS.Domain.ValueObjects.Name only allows letters/apostrophes/hyphens
        // (no spaces), so a first/last name containing a space in the real `students`
        // data will throw here. Flagging since this is a pre-existing constraint on
        // Name, not something introduced by this repository.
        var student = new Student(
            reader.GetString("student_id"),
            new Name(reader.GetString("first_name")),
            new Name(reader.GetString("last_name")));

        var middleNameOrdinal = reader.GetOrdinal("middle_name");
        if (!reader.IsDBNull(middleNameOrdinal))
            student.SetMiddleName(new Name(reader.GetString(middleNameOrdinal)));

        var suffixOrdinal = reader.GetOrdinal("suffix");
        if (!reader.IsDBNull(suffixOrdinal))
            student.SetSuffix(reader.GetString(suffixOrdinal));

        return student;
    }

    private static EnrolledStudentDTO MapRosterStudent(MySqlDataReader reader)
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