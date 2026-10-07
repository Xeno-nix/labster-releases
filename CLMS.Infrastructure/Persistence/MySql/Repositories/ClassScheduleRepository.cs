using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class ClassScheduleRepository(MySqlConnectionFactory connectionFactory) : IClassScheduleRepository
{
    // Columns shared by every schedule query. student_count is a correlated subquery because
    // a class's roster lives in student_enrollment keyed on the individual schedule row.
    private const string ScheduleColumns = """
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
                cs.time_end,
                cs.year_level,
                cs.section,
                (
                    SELECT COUNT(DISTINCT se.student_id)
                    FROM student_enrollment se
                    WHERE se.class_schedule_id = cs.class_schedule_id
                ) AS student_count
        """;

    private const string ScheduleJoins = """
            FROM class_schedule cs
            INNER JOIN courses c
                ON cs.course_id = c.course_id
            INNER JOIN users u
                ON cs.instructor_id = u.user_id
            INNER JOIN laboratories l
                ON cs.lab_id = l.lab_id
            INNER JOIN academic_terms at
                ON cs.term_id = at.academic_term_id
        """;

    public async Task<IEnumerable<ClassScheduleDTO>> GetAllAsync()
    {
        var schedules = new List<ClassScheduleDTO>();

        var sql = $"""
            SELECT
                {ScheduleColumns}
            {ScheduleJoins}
            WHERE u.role_type = 'Faculty'
              AND cs.deleted_at IS NULL
            ORDER BY
                cs.day_of_week,
                cs.time_start;
            """;

        await using var connection =
            connectionFactory.CreateConnection();

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

    /// <summary>
    /// Schedules taught by one instructor in one term. Unlike GetAllAsync this does not
    /// filter on the instructor's role_type, so an instructor is always visible even if
    /// their role changes.
    /// </summary>
    public async Task<IEnumerable<ClassScheduleDTO>> GetByInstructorAsync(
        Guid instructorId,
        int termId)
    {
        var schedules = new List<ClassScheduleDTO>();

        var sql = $"""
            SELECT
                {ScheduleColumns}
            {ScheduleJoins}
            WHERE cs.instructor_id = @InstructorId
              AND cs.term_id = @TermId
              AND cs.deleted_at IS NULL
            ORDER BY
                cs.year_level,
                cs.section,
                cs.time_start;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@InstructorId", instructorId);
        command.Parameters.AddWithValue("@TermId", termId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }

        return schedules;
    }

    /// <summary>
    /// Every non-deleted schedule in a term. Used by the Admin class list view.
    /// </summary>
    public async Task<IEnumerable<ClassScheduleDTO>> GetAllForTermAsync(int termId)
    {
        var schedules = new List<ClassScheduleDTO>();

        var sql = $"""
            SELECT
                {ScheduleColumns}
            {ScheduleJoins}
            WHERE cs.term_id = @TermId
              AND cs.deleted_at IS NULL
            ORDER BY
                cs.year_level,
                cs.section,
                c.course_code,
                cs.time_start;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@TermId", termId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }

        return schedules;
    }

    /// <summary>
    /// All schedule ids belonging to the same "class" as <paramref name="scheduleId"/>:
    /// same course, instructor, term, year level and section. The returned list always
    /// includes <paramref name="scheduleId"/> itself.
    /// <para>
    /// NULL-safe equality (<c>&lt;=&gt;</c>) is deliberate: a schedule with a NULL year
    /// level or section only matches rows that are also NULL in both, which makes it a
    /// group of one until those fields are filled in.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<int>> GetGroupScheduleIdsAsync(int scheduleId)
    {
        const string sql = """
            SELECT cs2.class_schedule_id
            FROM class_schedule cs
            INNER JOIN class_schedule cs2
                ON  cs2.course_id     =  cs.course_id
                AND cs2.instructor_id =  cs.instructor_id
                AND cs2.term_id       =  cs.term_id
                AND cs2.year_level   <=> cs.year_level
                AND cs2.section      <=> cs.section
            WHERE cs.class_schedule_id = @ScheduleId
              AND cs.deleted_at IS NULL
              AND cs2.deleted_at IS NULL;
            """;

        var ids = new List<int>();

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@ScheduleId", scheduleId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32("class_schedule_id"));
        }

        return ids;
    }

    /// <summary>
    /// Same grouping as <see cref="GetGroupScheduleIdsAsync"/> but returning the full rows
    /// (day/time, StudentCount, lab, ...) in week order. Used by the sync status UI.
    /// </summary>
    public async Task<IReadOnlyList<ClassScheduleDTO>> GetGroupSchedulesAsync(int scheduleId)
    {
        var schedules = new List<ClassScheduleDTO>();

        var sql = $"""
            SELECT
                {ScheduleColumns}
            {ScheduleJoins}
            INNER JOIN class_schedule cs2
                ON  cs.course_id     =  cs2.course_id
                AND cs.instructor_id =  cs2.instructor_id
                AND cs.term_id       =  cs2.term_id
                AND cs.year_level   <=> cs2.year_level
                AND cs.section      <=> cs2.section
            WHERE cs2.class_schedule_id = @ScheduleId
              AND cs.deleted_at IS NULL
              AND cs2.deleted_at IS NULL
            ORDER BY
                cs.day_of_week,
                cs.time_start;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@ScheduleId", scheduleId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }

        return schedules;
    }

    public async Task<ClassScheduleDTO?> GetByIdAsync(int id)
    {
        var sql = $"""
            SELECT
                {ScheduleColumns}
            {ScheduleJoins}
            WHERE cs.class_schedule_id = @Id
              AND u.role_type = 'Faculty'
              AND cs.deleted_at IS NULL;
            """;

        await using var connection =
            connectionFactory.CreateConnection();

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
        var sql = $"""
            SELECT
                {ScheduleColumns}
            {ScheduleJoins}

            INNER JOIN student_enrollment se
                ON se.class_schedule_id = cs.class_schedule_id

            WHERE se.student_id = @StudentId
            AND cs.day_of_week = @DayOfWeek
            AND cs.time_start <= @CurrentTime
            AND cs.time_end >= @CurrentTime
            AND cs.deleted_at IS NULL
            AND (at.start_date IS NULL OR at.start_date <= CURDATE())
            AND (at.end_date IS NULL OR at.end_date >= CURDATE())
            AND u.role_type = 'Faculty'

            LIMIT 1;
            """;

        await using var connection = connectionFactory.CreateConnection();
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
                term_id,
                year_level,
                section
            )
            VALUES
            (
                @CourseId,
                @InstructorId,
                @LaboratoryId,
                @DayOfWeek,
                @TimeStart,
                @TimeEnd,
                @AcademicTermId,
                @YearLevel,
                @Section
            );

            SELECT LAST_INSERT_ID();
            """;

        await using var connection =
            connectionFactory.CreateConnection();

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

        command.Parameters.AddWithValue(
            "@YearLevel",
            schedule.YearLevel is null
                ? DBNull.Value
                : schedule.YearLevel.Value);

        command.Parameters.AddWithValue(
            "@Section",
            (object?)schedule.Section ?? DBNull.Value);

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
                term_id = @AcademicTermId,
                year_level = @YearLevel,
                section = @Section
            WHERE class_schedule_id = @Id
              AND deleted_at IS NULL;
            """;

        await using var connection =
            connectionFactory.CreateConnection();

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

        command.Parameters.AddWithValue(
            "@YearLevel",
            schedule.YearLevel is null
                ? DBNull.Value
                : schedule.YearLevel.Value);

        command.Parameters.AddWithValue(
            "@Section",
            (object?)schedule.Section ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = """
            UPDATE class_schedule
            SET
                deleted_at = NOW()
            WHERE class_schedule_id = @Id
              AND deleted_at IS NULL;
            """;

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteNonQueryAsync();
    }

    private static ClassScheduleDTO MapSchedule(
        MySqlDataReader reader)
    {
        var yearLevelOrdinal = reader.GetOrdinal("year_level");
        var sectionOrdinal = reader.GetOrdinal("section");
        var studentCountOrdinal = reader.GetOrdinal("student_count");

        return new ClassScheduleDTO
        {
            Id = reader.GetInt32("class_schedule_id"),
            CourseId = reader.GetInt32("course_id"),
            CourseCode = reader.GetString("course_code"),
            CourseName = reader.GetString("course_name"),
            InstructorId = reader.GetGuid("instructor_id"),
            InstructorName = reader.GetString("instructor_name"),
            LaboratoryId = reader.GetInt32("lab_id"),
            LaboratoryName = reader.GetString("lab_name"),
            AcademicTermId = reader.GetInt32("term_id"),
            AcademicYear = reader.GetString("academic_year"),
            Semester = reader.GetString("semester"),
            DayOfWeek = Enum.Parse<DayOfWeek>(
                reader.GetString("day_of_week")),
            TimeStart = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("time_start")),
            TimeEnd = TimeOnly.FromTimeSpan(
                reader.GetTimeSpan("time_end")),

            // Both nullable: existing schedules have no year level or section yet.
            YearLevel = reader.IsDBNull(yearLevelOrdinal)
                ? null
                : reader.GetInt32(yearLevelOrdinal),
            Section = reader.IsDBNull(sectionOrdinal)
                ? null
                : reader.GetString(sectionOrdinal),

            StudentCount = reader.IsDBNull(studentCountOrdinal)
                ? 0
                : reader.GetInt32(studentCountOrdinal)
        };
    }
}