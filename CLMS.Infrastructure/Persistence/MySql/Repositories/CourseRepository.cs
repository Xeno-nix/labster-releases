using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class CourseRepository(MySqlConnectionFactory connectionFactory) : ICourseRepository
{
    public async Task<int> CreateAsync(Course course)
    {
        const string sql = "CALL sp_course_create(@Code, @Name);";
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Code", course.CourseCode);
        command.Parameters.AddWithValue("@Name", course.CourseName);
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("A course with this code already exists.", ex);
        }
    }

    public async Task<bool> UpdateAsync(Course course)
    {
        const string sql = "CALL sp_course_update(@Code, @Name, @Id);";
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", course.Id);
        command.Parameters.AddWithValue("@Code", course.CourseCode);
        command.Parameters.AddWithValue("@Name", course.CourseName);
        try { return await command.ExecuteAffectedRowsAsync() > 0; }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("A course with this code already exists.", ex);
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_course_delete(@Id);", connection);
        command.Parameters.AddWithValue("Id", id);
        try { return await command.ExecuteAffectedRowsAsync() > 0; }
        catch (MySqlException ex) when (ex.Number is 1451 or 1644)
        {
            throw new InvalidOperationException("This course is used by class schedules. Remove or reassign those schedules before deleting it.", ex);
        }
    }

    public async Task<IEnumerable<Course>> GetAllAsync()
    {
        var courses = new List<Course>();

        const string sql = "CALL sp_course_get_all();";

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
        const string sql = "CALL sp_course_get_by_id(@Id);";

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
