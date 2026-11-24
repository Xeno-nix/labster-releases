using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Infrastructure.Persistence.MySql;
using MySqlConnector;

namespace CLMS.Infrastructure.Repositories;

public class LabUtilizationRepository(MySqlConnectionFactory connectionFactory) : ILabUtilizationRepository
{
    public async Task<int> CreateAsync(LaboratoryUseLog log)
    {
        const string sql = "CALL sp_lab_utilization_create(@StudentId, @GuestId, @Purpose, @InstructorId, @LaboratoryId, @LogDate, @TimeIn, @TimeOut);";

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
        const string sql = "CALL sp_lab_utilization_update(@StudentId, @GuestId, @Purpose, @InstructorId, @LogDate, @TimeIn, @TimeOut, @Id);";

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

        await command.ExecuteAffectedRowsAsync();
    }

    public async Task<LaboratoryUseLog?> GetActiveSessionForStudentAsync(string studentId)
    {
        const string sql = "CALL sp_lab_utilization_get_active_session_for_student(@StudentId);";

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

        const string sql = "CALL sp_lab_utilization_get_active_sessions_for_guest_name(@GuestName);";

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
        const string sql = "CALL sp_lab_utilization_get_dashboard_stats(@FacultyId);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@FacultyId", (object?)facultyId?.ToString() ?? DBNull.Value);

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
