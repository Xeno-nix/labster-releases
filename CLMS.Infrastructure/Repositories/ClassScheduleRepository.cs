using CLMS.Application.DTOs.Booking;
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

    public async Task<IEnumerable<ClassScheduleDTO>> GetAllAsync()
    {
        var schedules = new List<ClassScheduleDTO>();

        const string sql = """
            SELECT
                cs.class_schedule_id,
                cs.course_id,
                c.course_code,
                c.course_name,
                cs.instructor_id,
                CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
                cs.lab_id,
                l.lab_name,
                cs.term_id,
                at.academic_year,
                at.semester,
                cs.day_of_week,
                cs.time_start,
                cs.time_end
            FROM class_schedule cs
            INNER JOIN courses c
                ON cs.course_id = c.course_id
            INNER JOIN users u
                ON cs.instructor_id = u.user_id
            INNER JOIN laboratories l
                ON cs.lab_id = l.lab_id
            INNER JOIN academic_terms at
                ON cs.term_id = at.academic_term_id
            WHERE u.role_type = 'Faculty'
              AND cs.is_deleted = FALSE
            ORDER BY
                cs.day_of_week,
                cs.time_start;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }

        return schedules;
    }

    public async Task<ClassScheduleDTO?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT
                cs.class_schedule_id,
                cs.course_id,
                c.course_code,
                c.course_name,
                cs.instructor_id,
                CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
                cs.lab_id,
                l.lab_name,
                cs.term_id,
                at.academic_year,
                at.semester,
                cs.day_of_week,
                cs.time_start,
                cs.time_end
            FROM class_schedule cs
            INNER JOIN courses c
                ON cs.course_id = c.course_id
            INNER JOIN users u
                ON cs.instructor_id = u.user_id
            INNER JOIN laboratories l
                ON cs.lab_id = l.lab_id
            INNER JOIN academic_terms at
                ON cs.term_id = at.academic_term_id
            WHERE cs.class_schedule_id = @Id
              AND u.role_type = 'Faculty'
              AND cs.is_deleted = FALSE;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapSchedule(reader);
    }

    public async Task<ClassScheduleDTO?> FindCurrentForStudentAsync(string studentId, DayOfWeek dayOfWeek, TimeOnly currentTime)
    {
        // Added for the logging terminal's Student Time-In flow. Joins through
        // student_enrollment to find the schedule this student is enrolled in that is
        // in session right now (day matches, current time within start/end).
        const string sql = """
            SELECT
                cs.class_schedule_id,
                cs.course_id,
                c.course_code,
                c.course_name,

                cs.instructor_id,
                CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,

                cs.lab_id,
                l.lab_name,

                cs.term_id,
                at.academic_year,
                at.semester,

                cs.day_of_week,
                cs.time_start,
                cs.time_end

            FROM class_schedule cs

            INNER JOIN student_enrollment se
                ON se.class_schedule_id = cs.class_schedule_id

            INNER JOIN courses c
                ON cs.course_id = c.course_id

            INNER JOIN users u
                ON cs.instructor_id = u.user_id

            INNER JOIN laboratories l
                ON cs.lab_id = l.lab_id

            INNER JOIN academic_terms at
                ON cs.term_id = at.academic_term_id

            WHERE se.student_id = @StudentId
            AND cs.day_of_week = @DayOfWeek
            AND cs.time_start <= @CurrentTime
            AND cs.time_end >= @CurrentTime
            AND u.role_type = 'Faculty'

            LIMIT 1;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@StudentId", studentId);
        command.Parameters.AddWithValue("@DayOfWeek", dayOfWeek.ToString());
        command.Parameters.AddWithValue("@CurrentTime", currentTime.ToTimeSpan());

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

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@CourseId",
            schedule.CourseId);

        command.Parameters.AddWithValue(
            "@InstructorId",
            schedule.InstructorId);

        command.Parameters.AddWithValue(
            "@LaboratoryId",
            schedule.LaboratoryId);

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
            WHERE class_schedule_id = @Id
              AND is_deleted = FALSE;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@Id",
            schedule.Id);

        command.Parameters.AddWithValue(
            "@CourseId",
            schedule.CourseId);

        command.Parameters.AddWithValue(
            "@InstructorId",
            schedule.InstructorId);

        command.Parameters.AddWithValue(
            "@LaboratoryId",
            schedule.LaboratoryId);

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
            UPDATE class_schedule
            SET
                is_deleted = TRUE,
                deleted_at = NOW()
            WHERE class_schedule_id = @Id
              AND is_deleted = FALSE;
            """;

        await using var connection = new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteNonQueryAsync();
    }

    private static ClassScheduleDTO MapSchedule(
        MySqlDataReader reader)
    {
        return new ClassScheduleDTO
        {
            Id = reader.GetInt32("class_schedule_id"),

            CourseId = reader.GetInt32("course_id"),

            CourseCode = reader.GetString("course_code"),

            CourseName = reader.GetString("course_name"),

            InstructorId = reader.GetGuid("instructor_id"),

            InstructorName =reader.GetString("instructor_name"),

            LaboratoryId =reader.GetInt32("lab_id"),

            LaboratoryName =reader.GetString("lab_name"),

            AcademicTermId =reader.GetInt32("term_id"),

            AcademicYear =reader.GetString("academic_year"),

            Semester =reader.GetString("semester"),

            DayOfWeek =Enum.Parse<DayOfWeek>(reader.GetString("day_of_week")),

            TimeStart =TimeOnly.FromTimeSpan(reader.GetTimeSpan("time_start")),

            TimeEnd =TimeOnly.FromTimeSpan(reader.GetTimeSpan("time_end"))
        };
    }
}