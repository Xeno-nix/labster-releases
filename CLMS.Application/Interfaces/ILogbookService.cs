using CLMS.Application.DTOs.Logbook;

namespace CLMS.Application.Interfaces;

public interface ILogbookService
{
    Task<LogbookPageResultDTO> GetLogsAsync(LogbookQueryDTO query);

    /// <summary>Builds the CSV export content for the current filters (all matching rows, no pagination).</summary>
    Task<string> ExportCsvAsync(LogbookQueryDTO query);
}
