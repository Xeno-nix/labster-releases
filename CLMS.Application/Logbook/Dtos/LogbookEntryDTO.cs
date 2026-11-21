namespace CLMS.Application.DTOs.Logbook;

public class LogbookEntryDTO
{
    public int Id { get; set; }

    public string StudentId { get; set; } = string.Empty;
    
    public string Name { get; set; } = string.Empty;

    public string? Purpose { get; set; }
    public string? Laboratory { get; set; }

    public DateOnly? Date { get; set; }
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }

    public bool IsActive => TimeOut is null;
}
