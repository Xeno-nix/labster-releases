using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class ClassScheduleRepository : IClassScheduleRepository
{
    private readonly string _connectionString;

    public ClassScheduleRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<ClassSchedule>> GetAllAsync()
    {
        var schedules = new List<ClassSchedule>();

        const string sql = """
            SELECT
                class_schedule_id,
                course_id,
                instructor_id,
                lab_id,
                term_id,
                day_of_week,
                time_start,
                time_end
            FROM class_schedule
            ORDER BY
                day_of_week,
                time_start;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }

        return schedules;
    }

    public async Task<ClassSchedule?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT
                class_schedule_id,
                course_id,
                instructor_id,
                lab_id,
                term_id,
                day_of_week,
                time_start,
                time_end
            FROM class_schedule
            WHERE class_schedule_id = @Id;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapSchedule(reader);
    }

    public async Task<int> CreateAsync(ClassSchedule schedule)
    {
        const string sql = """
            INSERT INTO class_schedule
            (
                course_id,
                instructor_id,
                lab_id,
                day_of_week,
                time_start,
                time_end,
                term_id
            )
            VALUES
            (
                @CourseId,
                @InstructorId,
                @LaboratoryId,
                @DayOfWeek,
                @TimeStart,
                @TimeEnd,
                @AcademicTermId
            );

            SELECT LAST_INSERT_ID();
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@CourseId", schedule.CourseId);
        command.Parameters.AddWithValue("@InstructorId", schedule.InstructorId);
        command.Parameters.AddWithValue("@LaboratoryId", schedule.LaboratoryId);
        command.Parameters.AddWithValue(
            "@DayOfWeek",
            schedule.DayOfWeek.ToString());
        command.Parameters.AddWithValue(
            "@TimeStart",
            schedule.TimeStart.ToTimeSpan());
        command.Parameters.AddWithValue(
            "@TimeEnd",
            schedule.TimeEnd.ToTimeSpan());
        command.Parameters.AddWithValue(
            "@AcademicTermId",
            schedule.AcademicTermId);

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(ClassSchedule schedule)
    {
        const string sql = """
            UPDATE class_schedule
            SET
                course_id = @CourseId,
                instructor_id = @InstructorId,
                lab_id = @LaboratoryId,
                day_of_week = @DayOfWeek,
                time_start = @TimeStart,
                time_end = @TimeEnd,
                term_id = @AcademicTermId
            WHERE class_schedule_id = @Id;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", schedule.Id);
        command.Parameters.AddWithValue("@CourseId", schedule.CourseId);
        command.Parameters.AddWithValue("@InstructorId", schedule.InstructorId);
        command.Parameters.AddWithValue("@LaboratoryId", schedule.LaboratoryId);
        command.Parameters.AddWithValue(
            "@DayOfWeek",
            schedule.DayOfWeek.ToString());
        command.Parameters.AddWithValue(
            "@TimeStart",   
            schedule.TimeStart.ToTimeSpan());
        command.Parameters.AddWithValue(
            "@TimeEnd",
            schedule.TimeEnd.ToTimeSpan());
        command.Parameters.AddWithValue(
            "@AcademicTermId",
            schedule.AcademicTermId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = """
            DELETE FROM class_schedule
            WHERE class_schedule_id = @Id;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteNonQueryAsync();
    }

    private static ClassSchedule MapSchedule(
        MySqlDataReader reader)
    {
        return new ClassSchedule
        {
            Id = reader.GetInt32("class_schedule_id"),

            CourseId = reader.GetInt32("course_id"),

            InstructorId = reader.GetGuid("instructor_id"),

            LaboratoryId = reader.GetInt32("lab_id"),

            AcademicTermId = reader.GetInt32("term_id"),

            DayOfWeek = Enum.Parse<DayOfWeek>(
                reader.GetString("day_of_week")),

            TimeStart = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("time_start")),

            TimeEnd = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("time_end"))
        };
    }
}