using CLMS.Application.DTOs.Logbook;

namespace CLMS.Application.Interfaces.Repositories;

public interface ILogbookRepository
{
    /// <summary>Mirrors LoggingService::getLogs() — filtered, sorted, paginated.</summary>
    Task<(List<LogbookEntryDTO> Entries, int TotalCount, bool SupportsLaboratory)> GetPagedAsync(LogbookQueryDTO query);

    /// <summary>Mirrors LoggingService::buildLogQuery() with no pagination — used for CSV export.</summary>
    Task<List<LogbookEntryDTO>> GetAllFilteredAsync(LogbookQueryDTO query);
}
