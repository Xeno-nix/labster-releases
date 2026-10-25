using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class LabUtilizationRepository(MySqlConnectionFactory connectionFactory) : ILabUtilizationRepository
{
    public async Task<int> CreateAsync(LaboratoryUseLog log)
    {
        const string sql = """
            INSERT INTO attendance_logs
            (
                student_id,
                guest_id,
                purpose_subject,
                faculty_in_charge,
                laboratory_id,
                log_date,
                time_in,
                time_out
            )
            VALUES
            (
                @StudentId,
                @GuestId,
                @Purpose,
                @InstructorId,
                @LaboratoryId,
                @LogDate,
                @TimeIn,
                @TimeOut
            );

            SELECT LAST_INSERT_ID();
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@StudentId", (object?)log.StudentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@GuestId", (object?)log.GuestId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Purpose", log.Purpose);
        command.Parameters.AddWithValue("@InstructorId", log.InstructorId.HasValue ? log.InstructorId.Value.ToString() : (object)DBNull.Value);
        command.Parameters.AddWithValue("@LogDate", log.LogDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@TimeIn", log.TimeIn);
        command.Parameters.AddWithValue("@TimeOut", (object?)log.TimeOut ?? DBNull.Value);
        command.Parameters.AddWithValue("@LaboratoryId", (object?)log.LaboratoryId ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync();

        var id = Convert.ToInt32(result);
        log.Id = id;
        return id;
    }

    public async Task UpdateAsync(LaboratoryUseLog log)
    {
        const string sql = """
            UPDATE attendance_logs
            SET
                student_id = @StudentId,
                guest_id = @GuestId,
                purpose_subject = @Purpose,
                faculty_in_charge = @InstructorId,
                log_date = @LogDate,
                time_in = @TimeIn,
                time_out = @TimeOut
            WHERE utilization_log_id = @Id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", log.Id);
        command.Parameters.AddWithValue("@StudentId", (object?)log.StudentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@GuestId", (object?)log.GuestId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Purpose", log.Purpose);
        command.Parameters.AddWithValue("@InstructorId", log.InstructorId.HasValue ? log.InstructorId.Value.ToString() : (object)DBNull.Value);
        command.Parameters.AddWithValue("@LogDate", log.LogDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@TimeIn", log.TimeIn);
        command.Parameters.AddWithValue("@TimeOut", (object?)log.TimeOut ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<LaboratoryUseLog?> GetActiveSessionForStudentAsync(string studentId)
    {
        const string sql = """
            SELECT
                utilization_log_id,
                student_id,
                guest_id,
                purpose_subject,
                faculty_in_charge,
                log_date,
                time_in,
                time_out
            FROM attendance_logs
            WHERE student_id = @StudentId
            AND time_out IS NULL
            ORDER BY time_in DESC
            LIMIT 1;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@StudentId", studentId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapLog(reader);
    }

    public async Task<List<LaboratoryUseLog>> GetActiveSessionsForGuestNameAsync(string guestName)
    {
        var results = new List<LaboratoryUseLog>();

        const string sql = """
            SELECT
                al.utilization_log_id,
                al.student_id,
                al.guest_id,
                al.purpose_subject,
                al.faculty_in_charge,
                al.log_date,
                al.time_in,
                al.time_out
            FROM attendance_logs al
            INNER JOIN guests g
                ON al.guest_id = g.guest_id
            WHERE g.guest_name = @GuestName
            AND al.time_out IS NULL
            ORDER BY al.time_in DESC;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@GuestName", guestName);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(MapLog(reader));
        }

        return results;
    }

    public async Task<(int ActiveNow, int SessionsToday, List<(int LaboratoryId, int Active)> ByLab)>
        GetDashboardStatsAsync(Guid? facultyId)
    {
        // Faculty scope: faculty_in_charge stores the users.user_id GUID as text.
        var facultyClause = facultyId.HasValue
            ? " AND faculty_in_charge = @FacultyId"
            : string.Empty;

        var sql = $"""
            SELECT
                laboratory_id,
                COALESCE(SUM(time_out IS NULL), 0) AS active_now,
                COALESCE(SUM(log_date = CURDATE()), 0) AS sessions_today
            FROM attendance_logs
            WHERE 1 = 1{facultyClause}
            GROUP BY laboratory_id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        if (facultyId.HasValue)
            command.Parameters.AddWithValue("@FacultyId", facultyId.Value.ToString());

        int activeNow = 0;
        int sessionsToday = 0;
        var byLab = new List<(int LaboratoryId, int Active)>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var active = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("active_now")));
            var today = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("sessions_today")));

            activeNow += active;
            sessionsToday += today;

            // Rows written before the course/laboratory_id migration have no lab —
            // they still count toward the totals, just not toward any lab bar.
            var labOrdinal = reader.GetOrdinal("laboratory_id");
            if (!reader.IsDBNull(labOrdinal) && active > 0)
                byLab.Add((reader.GetInt32(labOrdinal), active));
        }

        return (activeNow, sessionsToday, byLab);
    }

    private static LaboratoryUseLog MapLog(MySqlDataReader reader)
    {
        var studentIdOrdinal = reader.GetOrdinal("student_id");
        var guestIdOrdinal = reader.GetOrdinal("guest_id");
        var instructorOrdinal = reader.GetOrdinal("faculty_in_charge");
        var timeOutOrdinal = reader.GetOrdinal("time_out");

        return new LaboratoryUseLog
        {
            Id = reader.GetInt32("utilization_log_id"),
            StudentId = reader.IsDBNull(studentIdOrdinal) ? null : reader.GetString(studentIdOrdinal),
            GuestId = reader.IsDBNull(guestIdOrdinal) ? null : reader.GetInt32(guestIdOrdinal),
            Purpose = reader.GetString("purpose_subject"),
            InstructorId = reader.IsDBNull(instructorOrdinal) ? null : Guid.Parse(reader.GetString(instructorOrdinal)),
            LogDate = DateOnly.FromDateTime(reader.GetDateTime("log_date")),
            TimeIn = reader.GetDateTime("time_in"),
            TimeOut = reader.IsDBNull(timeOutOrdinal) ? null : reader.GetDateTime(timeOutOrdinal),
        };
    }
}
