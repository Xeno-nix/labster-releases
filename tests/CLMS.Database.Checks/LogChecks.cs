using CLMS.Application.DTOs.Logbook;
using CLMS.Domain.Entities.UtilizationLogging;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Infrastructure.Repositories;
using Dapper;
using MySqlConnector;
using static DatabaseChecks;

internal static class LogChecks
{
    internal static async Task Run(MySqlConnection db, MySqlConnectionFactory factory, Guid faculty, string student, Guid unit, int guest)
    {
        var logs = new LabUtilizationRepository(factory);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var log = new LaboratoryUseLog { StudentId = student, InstructorId = faculty, LaboratoryId = 1,
            Purpose = "Integration session", LogDate = today, TimeIn = DateTime.UtcNow };
        log.Id = await logs.CreateAsync(log);
        Require((await logs.GetActiveSessionForStudentAsync(student))?.Id == log.Id, "attendance creation and active student lookup");
        var visitor = new LaboratoryUseLog { GuestId = guest, InstructorId = faculty, LaboratoryId = 1,
            Purpose = "Visitor session", LogDate = today, TimeIn = DateTime.UtcNow };
        await logs.CreateAsync(visitor);
        Require((await logs.GetActiveSessionsForGuestNameAsync("Test visitor")).Count == 1, "active guest lookup");
        var dashboard = await logs.GetDashboardStatsAsync(faculty);
        Require(dashboard.ActiveNow == 2 && dashboard.SessionsToday == 2 && dashboard.ByLab.Single().Active == 2, "faculty dashboard procedure aggregation");
        Require((await logs.GetDashboardStatsAsync(null)).ActiveNow == 2, "unscoped dashboard uses SQL NULL");
        var logbook = new LogbookRepository(factory);
        var page = await logbook.GetPagedAsync(new LogbookQueryDTO { Page = 1, PageSize = 1 });
        Require(page.TotalCount == 2 && page.Entries.Count == 1, "logbook count and page result sets");
        foreach (var filter in new[] { "day", "week", "month" })
            Require((await logbook.GetAllFilteredAsync(new LogbookQueryDTO { Search = student, Filter = filter })).Count == 1, "logbook search + " + filter);
        log.TimeOut = DateTime.UtcNow;
        await logs.UpdateAsync(log);
        Require(await logs.GetActiveSessionForStudentAsync(student) is null, "attendance end closes active session");

        await db.ExecuteAsync("INSERT INTO unit_utilization_logs(computer_unit_id,student_id,log_date,time_in) VALUES(@unit,@student,CURDATE(),UTC_TIMESTAMP())", new { unit, student });
        var usage = await db.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        var monitoring = new UnitMonitoringRepository(factory);
        Require((await monitoring.GetAllAsync(default)).Single().CurrentUser?.Contains("Jane") == true, "monitoring latest usage DTO");
        Require(await Scalar(db, "SELECT COUNT(*) FROM unit_activity WHERE activity_type='usage_started' AND is_resolved IS NULL") == 1, "usage trigger adds informational activity");
        await db.ExecuteAsync("UPDATE unit_utilization_logs SET time_out=UTC_TIMESTAMP() WHERE unit_utilization_log_id=@usage", new { usage });
        Require(await Scalar(db, "SELECT COUNT(*) FROM unit_activity WHERE activity_type='usage_ended'") == 1, "usage end activity");
        var activity = await Scalar(db, "SELECT COUNT(*) FROM unit_activity");
        await using (var tx = await db.BeginTransactionAsync())
        {
            await db.ExecuteAsync("UPDATE unit_utilization_logs SET time_out=NULL WHERE unit_utilization_log_id=@usage", new { usage }, tx);
            await tx.RollbackAsync();
        }
        Require(await Scalar(db, "SELECT COUNT(*) FROM unit_activity") == activity, "usage rollback removes activity");
        await db.ExecuteAsync("DELETE FROM unit_utilization_logs WHERE unit_utilization_log_id=@usage", new { usage });
    }
}
