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
        await CheckLogbook(db, logbook, student);
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

    private static async Task CheckLogbook(MySqlConnection db, LogbookRepository logbook, string student)
    {
        await db.ExecuteAsync("INSERT INTO laboratories(lab_id,lab_name) VALUES(2,'Second lab')");
        // Same time_in exercises the primary-key tie-break across page boundaries.
        await db.ExecuteAsync("""
            INSERT INTO attendance_logs(laboratory_id,student_id,purpose_subject,log_date,time_in)
            VALUES (1,@student,'Boundary fixture',CURDATE(),CURDATE()),
                   (1,@student,'Boundary fixture',CURDATE(),CURDATE()),
                   (2,@student,'Boundary fixture',CURDATE(),CURDATE()),
                   (NULL,NULL,'Historical fixture',NULL,NULL)
            """, new { student });
        var first = await logbook.GetPagedAsync(new() { Search = "Boundary fixture", LaboratoryId = 1, PageSize = 1 });
        var second = await logbook.GetPagedAsync(new() { Search = "Boundary fixture", LaboratoryId = 1, PageSize = 1, Page = 2 });
        var export = await logbook.GetAllFilteredAsync(new() { Search = "Boundary fixture", LaboratoryId = 1 });
        Require(first.TotalCount == 2 && first.Entries.Single().Id > second.Entries.Single().Id,
            "laboratory paging has deterministic ties and scoped count");
        Require(export.Select(x => x.Id).SequenceEqual(new[] { first.Entries.Single().Id, second.Entries.Single().Id })
            && export.All(x => x.Laboratory == "Integration lab"), "export matches laboratory scope, order and names");
        var empty = await logbook.GetPagedAsync(new() { LaboratoryId = 999, PageSize = 1 });
        Require(empty.TotalCount == 0 && empty.Entries.Count == 0, "empty laboratory returns count and entries");
        var beyond = await logbook.GetPagedAsync(new() { Search = "Boundary fixture", Page = int.MaxValue, PageSize = 10 });
        Require(beyond.TotalCount == 3 && beyond.Entries.Count == 0, "large page offset does not overflow");
        Require((await logbook.GetAllFilteredAsync(new() { Search = "' OR 1=1 --" })).Count == 0, "search stays parameterized");
        var history = (await logbook.GetAllFilteredAsync(new() { Search = "Historical fixture" })).Single();
        Require(history.Date is null && history.TimeIn is null && history.Laboratory is null, "nullable historical rows survive view joins");
        try { await logbook.GetPagedAsync(new() { PageSize = 0 }); throw new Exception("Invalid page size accepted"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS invalid page size rejected"); }
        try { await db.ExecuteAsync("CALL sp_logbook_get_paged(NULL,NULL,0,1,NULL)"); throw new Exception("Invalid SQL page accepted"); }
        catch (MySqlException ex) when (ex.SqlState == "45000") { Console.WriteLine("PASS procedure rejects invalid page"); }

        // Compare range filtering with the former semantics at every date around New Year.
        await db.ExecuteAsync("""
            INSERT INTO attendance_logs(purpose_subject,log_date,time_in)
            WITH RECURSIVE dates AS (
                SELECT DATE('2026-12-20') AS d
                UNION ALL SELECT DATE_ADD(d,INTERVAL 1 DAY) FROM dates WHERE d<'2027-01-10'
            ) SELECT 'Date boundary fixture',d,d FROM dates
            """);
        Require(await Scalar(db, """
            SELECT COUNT(*) FROM attendance_logs
            WHERE purpose_subject='Date boundary fixture' AND
              ((YEARWEEK(log_date,1)=YEARWEEK('2027-01-01',1)) <>
              (log_date>=sf_logbook_period_start('week','2027-01-01') AND
               log_date<DATE_ADD(sf_logbook_period_start('week','2027-01-01'),INTERVAL 7 DAY)))
            """) == 0, "week ranges match YEARWEEK across New Year");
        await db.ExecuteAsync("DELETE FROM attendance_logs WHERE purpose_subject IN ('Boundary fixture','Historical fixture','Date boundary fixture')");
    }
}
