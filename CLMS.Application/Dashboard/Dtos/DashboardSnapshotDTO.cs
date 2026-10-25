namespace CLMS.Application.DTOs.Dashboard;

/// <summary>
/// Everything the dashboard renders, composed in one pass by DashboardService.
/// </summary>
public class DashboardSnapshotDTO
{
    public int ActiveNow { get; set; }
    public int SessionsToday { get; set; }
    public int ClassesRunningNow { get; set; }
    public int AttentionCount { get; set; }

    public List<LabOccupancyDTO> Occupancy { get; set; } = new();
    public List<TimelineItemDTO> Timeline { get; set; } = new();
    public List<AttentionItemDTO> Attention { get; set; } = new();
}

public class LabOccupancyDTO
{
    public string LabName { get; set; } = string.Empty;
    public int Active { get; set; }
    public int TotalUnits { get; set; }

    /// <summary>Active / TotalUnits, clamped to 100; falls back to 100 when a lab has no registered units but has people.</summary>
    public int Percent { get; set; }
}

public class TimelineItemDTO
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string LabName { get; set; } = string.Empty;
    public bool IsClass { get; set; }
    public bool IsNow { get; set; }
}

public class AttentionItemDTO
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Href { get; set; } = string.Empty;
}
