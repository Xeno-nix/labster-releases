using CLMS.Application.DTOs.Dashboard;
using CLMS.Application.Interfaces.Repositories;

namespace CLMS.Application.Services;

/// <summary>
/// Composes the dashboard's single snapshot: attendance aggregates, today's
/// schedule/bookings, per-lab occupancy, and the inventory attention list.
/// </summary>
public class DashboardService
{
    private readonly ILabUtilizationRepository _labUtilizationRepository;
    private readonly IClassScheduleRepository _classScheduleRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ILaboratoryRepository _laboratoryRepository;
    private readonly IComputerUnitRepository _computerUnitRepository;
    private readonly ISupplyRepository _supplyRepository;
    private readonly IEquipmentRepository _equipmentRepository;

    public DashboardService(
        ILabUtilizationRepository labUtilizationRepository,
        IClassScheduleRepository classScheduleRepository,
        IBookingRepository bookingRepository,
        ILaboratoryRepository laboratoryRepository,
        IComputerUnitRepository computerUnitRepository,
        ISupplyRepository supplyRepository,
        IEquipmentRepository equipmentRepository)
    {
        _labUtilizationRepository = labUtilizationRepository;
        _classScheduleRepository = classScheduleRepository;
        _bookingRepository = bookingRepository;
        _laboratoryRepository = laboratoryRepository;
        _computerUnitRepository = computerUnitRepository;
        _supplyRepository = supplyRepository;
        _equipmentRepository = equipmentRepository;
    }

    /// <param name="facultyId">null = admin (everything); otherwise scope to this instructor.</param>
    public async Task<DashboardSnapshotDTO> GetSnapshotAsync(Guid? facultyId)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        var (activeNow, sessionsToday, activeByLab) =
            await _labUtilizationRepository.GetDashboardStatsAsync(facultyId);

        var schedules = (await _classScheduleRepository.GetAllAsync())
            .Where(s => (facultyId is null || s.InstructorId == facultyId)
                        && s.DayOfWeek == now.DayOfWeek)
            .ToList();

        var bookings = (await _bookingRepository.GetAllAsync())
            .Where(b => b.Date == today)
            .ToList();

        var supplies = (await _supplyRepository.GetAllAsync()).ToList();
        var equipment = (await _equipmentRepository.GetAllAsync()).ToList();

        // Attention: low/out-of-stock supplies + equipment in maintenance or damaged.
        var attention = new List<AttentionItemDTO>();

        var lowSupplies = supplies.Count(s => s.Status is "low_stock" or "out_of_stock");
        if (lowSupplies > 0)
            attention.Add(new AttentionItemDTO
            {
                Label = "Supplies low or out of stock",
                Count = lowSupplies,
                Href = "/inventory?tab=supplies"
            });

        var brokenEquipment = equipment.Count(e => e.Status is "maintenance" or "damaged");
        if (brokenEquipment > 0)
            attention.Add(new AttentionItemDTO
            {
                Label = "Equipment in maintenance or damaged",
                Count = brokenEquipment,
                Href = "/inventory?tab=equipment"
            });

        // Occupancy per lab: active sessions vs registered computer units.
        var labs = (await _laboratoryRepository.GetAllAsync()).ToList();
        var units = (await _computerUnitRepository.GetAllAsync()).ToList();
        var activeByLabDict = activeByLab.ToDictionary(x => x.LaboratoryId, x => x.Active);
        var unitsByLab = units.GroupBy(u => u.LaboratoryId).ToDictionary(g => g.Key, g => g.Count());

        var occupancy = labs.Select(l =>
        {
            var active = activeByLabDict.GetValueOrDefault(l.Id);
            var totalUnits = unitsByLab.GetValueOrDefault(l.Id);
            return new LabOccupancyDTO
            {
                LabName = l.Name,
                Active = active,
                TotalUnits = totalUnits,
                Percent = totalUnits > 0
                    ? Math.Min(100, active * 100 / totalUnits)
                    : (active > 0 ? 100 : 0)
            };
        })
        .OrderByDescending(l => l.Active)
        .ToList();

        // Today's timeline: classes merged with bookings, sorted by start time.
        var timeline = new List<TimelineItemDTO>();

        timeline.AddRange(schedules.Select(s => new TimelineItemDTO
        {
            StartTime = s.TimeStart,
            EndTime = s.TimeEnd,
            Title = $"{s.CourseCode} - {s.CourseName}",
            Subtitle = s.InstructorName,
            LabName = s.LaboratoryName,
            IsClass = true,
            IsNow = s.TimeStart <= currentTime && s.TimeEnd >= currentTime
        }));

        timeline.AddRange(bookings.Select(b => new TimelineItemDTO
        {
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            Title = b.Purpose,
            Subtitle = b.RequestorName,
            LabName = b.LaboratoryName,
            IsClass = false,
            IsNow = b.StartTime <= currentTime && b.EndTime >= currentTime
        }));

        timeline = timeline.OrderBy(t => t.StartTime).ToList();

        return new DashboardSnapshotDTO
        {
            ActiveNow = activeNow,
            SessionsToday = sessionsToday,
            ClassesRunningNow = schedules.Count(s => s.TimeStart <= currentTime && s.TimeEnd >= currentTime),
            AttentionCount = attention.Sum(a => a.Count),
            Occupancy = occupancy,
            Timeline = timeline,
            Attention = attention
        };
    }
}
