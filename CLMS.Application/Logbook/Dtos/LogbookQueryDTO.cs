namespace CLMS.Application.DTOs.Logbook;

public class LogbookQueryDTO
{
    public string? Search { get; set; }
    
    public string? Filter { get; set; }
    public int? LaboratoryId { get; set; }

    public int Page { get; set; } = 1;
    
    public int PageSize { get; set; } = 10;
}
