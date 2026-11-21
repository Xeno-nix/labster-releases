using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using System.Text;
using CLMS.Application.DTOs.Logbook;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;

namespace CLMS.Application.Services;

/// <summary>Port of the admin logbook half of Laravel's LogbookController/LoggingService.</summary>
public class LogbookService 
{
    private readonly ICurrentUser actor;
    private readonly ILogbookRepository _repository;

    public LogbookService(ILogbookRepository repository, ICurrentUser actor)
    {
        this.actor = actor;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<LogbookPageResultDTO> GetLogsAsync(LogbookQueryDTO query)
    {
        await actor.RequirePrivilegeAsync(Privileges.LogbookView);
        var (entries, totalCount) = await _repository.GetPagedAsync(query).ConfigureAwait(false);

        return new LogbookPageResultDTO
        {
            Entries = entries,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    // Laravel: LogbookController::exportCsv() — builds the same CSV column layout.
    public async Task<string> ExportCsvAsync(LogbookQueryDTO query)
    {
        await actor.RequirePrivilegeAsync(Privileges.LogbookExport);
        var entries = await _repository.GetAllFilteredAsync(query).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", CsvField("Date"), CsvField("Student ID"), CsvField("Name"),
            CsvField("Purpose"), CsvField("Time In"), CsvField("Time Out")));

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                CsvField(e.Date?.ToString("yyyy-MM-dd") ?? string.Empty),
                CsvField(e.StudentId),
                CsvField(e.Name),
                CsvField(e.Purpose ?? string.Empty),
                CsvField(e.TimeIn?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
                CsvField(e.TimeOut?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty)));
        }

        return sb.ToString();
    }

    private static string CsvField(string value)
    {
        // Quote every field and escape embedded quotes — matches PHP's fputcsv defaults closely
        // enough for this data (no embedded newlines expected in these columns).
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
