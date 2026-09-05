namespace CLMS.Application.DTOs.Logbook;

/// <summary>
/// Mirrors the query params LogbookController::logs() reads off the request
/// (?search, ?filter, ?page). There is no ?lab_id here — see LogbookEntryDTO remarks
/// for why the laboratory column/filter isn't reproduced.
/// </summary>
public class LogbookQueryDTO
{
    public string? Search { get; set; }

    /// <summary>"day" | "week" | "month" | null (all time) — same values as Laravel's ?filter.</summary>
    public string? Filter { get; set; }

    public int Page { get; set; } = 1;

    /// <summary>Matches Laravel's ->paginate(10).</summary>
    public int PageSize { get; set; } = 10;
}
