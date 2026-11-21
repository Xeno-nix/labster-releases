using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
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
        var (entries, totalCount, supportsLaboratory) = await _repository.GetPagedAsync(query).ConfigureAwait(false);

        return new LogbookPageResultDTO
        {
            Entries = entries,
            TotalCount = totalCount,
            SupportsLaboratory = supportsLaboratory,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<List<LogbookEntryDTO>> GetExportEntriesAsync(LogbookQueryDTO query)
    {
        await actor.RequirePrivilegeAsync(Privileges.LogbookExport);
        return await _repository.GetAllFilteredAsync(query).ConfigureAwait(false);
    }
}
