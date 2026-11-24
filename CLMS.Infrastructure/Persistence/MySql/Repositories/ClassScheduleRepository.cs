using System.Text.Json;
using System.Data.Common;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class ClassScheduleRepository(MySqlConnectionFactory connectionFactory) : IClassScheduleRepository
{
    public async Task<IEnumerable<ClassScheduleDTO>> GetAllAsync()
    {
        var schedules = new List<ClassScheduleDTO>();

        const string sql = "CALL sp_class_schedule_get_all();";

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

        const string sql = "CALL sp_class_schedule_get_by_instructor(@InstructorId, @TermId);";

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

        const string sql = "CALL sp_class_schedule_get_all_for_term(@TermId);";

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
        const string sql = "CALL sp_class_schedule_get_group_schedule_ids(@ScheduleId);";

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

        const string sql = "CALL sp_class_schedule_get_group_schedules(@ScheduleId);";

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
        const string sql = "CALL sp_class_schedule_get_by_id(@Id);";

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
        const string sql = "CALL sp_class_schedule_find_current_for_student(@StudentId, @DayOfWeek, @CurrentTime);";

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
        const string sql = "CALL sp_class_schedule_create(@CourseId, @InstructorId, @LaboratoryId, @DayOfWeek, @TimeStart, @TimeEnd, @AcademicTermId, @YearLevel, @Section);";

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
        const string sql = "CALL sp_class_schedule_update(@CourseId, @InstructorId, @LaboratoryId, @DayOfWeek, @TimeStart, @TimeEnd, @AcademicTermId, @YearLevel, @Section, @Id);";

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

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "CALL sp_class_schedule_delete(@Id);";

        await using var connection =
            connectionFactory.CreateConnection();

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteAffectedRowsAsync();
    }

    /// <summary>
    /// Soft-deletes a batch of schedules in one statement (soft-deleted rows are skipped).
    /// Runs on the caller's connection/transaction so it can join the delete-class-list
    /// transaction; the caller guarantees the concrete MySQL types.
    /// </summary>
    public async Task<int> DeleteManyAsync(
        IEnumerable<int> scheduleIds,
        DbConnection connection,
        DbTransaction transaction)
    {
        var affected = 0;
        foreach (var batch in scheduleIds.Distinct().Chunk(500))
        {
            await using var command = new MySqlCommand("CALL sp_class_schedule_delete_many(@ids);",
                (MySqlConnection)connection, (MySqlTransaction)transaction);
            command.Parameters.AddWithValue("ids", JsonSerializer.Serialize(batch));
            affected += await command.ExecuteAffectedRowsAsync();
        }
        return affected;
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