using CLMS.Application.DTOs.Logbook;

namespace CLMS.Application.Interfaces;

public interface ILogbookService
{
    Task<LogbookPageResultDTO> GetLogsAsync(LogbookQueryDTO query);
    
    Task<string> ExportCsvAsync(LogbookQueryDTO query);
}
