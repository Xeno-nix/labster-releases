using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class CourseRepository : ICourseRepository
{
    private readonly string _connectionString;

    public CourseRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Course>> GetAllAsync()
    {
        var courses = new List<Course>();

        const string sql = """
            SELECT
                course_id,
                course_code,
                course_name
            FROM courses
            ORDER BY course_name;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            courses.Add(MapCourse(reader));
        }

        return courses;
    }

    public async Task<Course?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT
                course_id,
                course_code,
                course_name
            FROM courses
            WHERE course_id = @Id;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapCourse(reader);
    }

    private static Course MapCourse(MySqlDataReader reader)
    {
        return new Course
        {
            Id = reader.GetInt32("course_id"),
            CourseCode = reader.GetString("course_code"),
            CourseName = reader.GetString("course_name")
        };
    }   

}

        