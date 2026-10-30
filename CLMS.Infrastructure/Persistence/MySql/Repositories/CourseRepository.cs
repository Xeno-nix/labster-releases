using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class CourseRepository(MySqlConnectionFactory connectionFactory) : ICourseRepository
{
    public async Task<int> CreateAsync(Course course)
    {
        const string sql = "INSERT INTO courses (course_code, course_name) VALUES (@Code, @Name);";
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Code", course.CourseCode);
        command.Parameters.AddWithValue("@Name", course.CourseName);
        try
        {
            await command.ExecuteNonQueryAsync();
            return checked((int)command.LastInsertedId);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("A course with this code already exists.", ex);
        }
    }

    public async Task<bool> UpdateAsync(Course course)
    {
        const string sql = "UPDATE courses SET course_code = @Code, course_name = @Name WHERE course_id = @Id;";
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", course.Id);
        command.Parameters.AddWithValue("@Code", course.CourseCode);
        command.Parameters.AddWithValue("@Name", course.CourseName);
        try { return await command.ExecuteNonQueryAsync() > 0; }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("A course with this code already exists.", ex);
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new MySqlCommand("SELECT course_id FROM courses WHERE course_id = @Id FOR UPDATE;", connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        if (await command.ExecuteScalarAsync() is null) return false;

        command.CommandText = "SELECT EXISTS(SELECT 1 FROM class_schedule WHERE course_id = @Id);";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("This course is used by class schedules. Remove or reassign those schedules before deleting it.");

        command.CommandText = "DELETE FROM courses WHERE course_id = @Id;";
        try
        {
            var deleted = await command.ExecuteNonQueryAsync() > 0;
            await transaction.CommitAsync();
            return deleted;
        }
        catch (MySqlException ex) when (ex.Number == 1451)
        {
            throw new InvalidOperationException("This course is in use and cannot be deleted.", ex);
        }
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

        await using var connection = connectionFactory.CreateConnection();
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

        await using var connection = connectionFactory.CreateConnection();
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
