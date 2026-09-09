using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.ValueObjects;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class StudentRepository : IStudentRepository
{
    private readonly string _connectionString;

    public StudentRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

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

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@StudentId", studentId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapStudent(reader);
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
}
