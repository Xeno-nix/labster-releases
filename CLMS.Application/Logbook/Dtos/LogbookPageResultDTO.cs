namespace CLMS.Application.DTOs.Logbook;

public class LogbookPageResultDTO
{
    public List<LogbookEntryDTO> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool SupportsLaboratory { get; set; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
