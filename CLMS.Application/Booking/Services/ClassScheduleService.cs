using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Services;

public class ClassScheduleService 
{
    private readonly ICurrentUser actor;
    private readonly IClassScheduleRepository _repository;
    private readonly ClassListService _classListService;
    

    public ClassScheduleService(
        IClassScheduleRepository repository,
        ClassListService classListService, ICurrentUser actor)
    {
        this.actor = actor;
        _repository = repository;
        _classListService = classListService;
    }

//create sched
    public async Task<int> CreateAsync(UpsertClassScheduleDTO dto)
    {
        await actor.RequirePrivilegeAsync(Privileges.SchedulesManage);
        var schedule = new ClassSchedule
        {
            CourseId = dto.CourseId,
            InstructorId = dto.InstructorId,
            LaboratoryId = dto.LaboratoryId,
            AcademicTermId = dto.AcademicTermId,
            YearLevel = dto.YearLevel,
            Section = NormalizeSection(dto.Section),
            DayOfWeek = dto.DayOfWeek,
            TimeStart = dto.TimeStart,
            TimeEnd = dto.TimeEnd
        };

        if (await CheckScheduleConflictAsync(dto))
        {
            throw new InvalidOperationException("The Class Schedule conflicts with an existing class schedule.");
        }

        var id = await _repository.CreateAsync(schedule);

        // Sync hook: a newly added meeting of an existing class inherits its siblings'
        // roster right after save, so the group never drifts. No-op for a brand-new class.
        await _classListService.SyncGroupEnrollmentAsync(id);

        return id;
    }

    /// <summary>
    /// Section is free text (max 10 chars in the schema): trimmed and uppercased so that
    /// "a" and " A " group together as the same class. Returns null for blank input.
    /// </summary>
    private static string? NormalizeSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return null;

        return section.Trim().ToUpperInvariant();
    }
//delete sched
    public async Task DeleteAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.SchedulesManage);
        await _repository.DeleteAsync(id);
    }

//get all sched
    public async Task<IEnumerable<ClassScheduleDTO>> GetAllAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.SchedulesView);
        return await _repository.GetAllAsync();
    }



    public async Task<ClassScheduleDTO?> GetByIdAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.SchedulesView);
        return  await _repository.GetByIdAsync(id);
    }

    public async Task<bool> CheckScheduleConflictAsync(UpsertClassScheduleDTO dto, int? excludingClassScheduleId = null)
    {
        await actor.RequirePrivilegeAsync(Privileges.SchedulesManage);
        var allSchedules = await _repository.GetAllAsync();

        return allSchedules
        .Where(s => s.LaboratoryId == dto.LaboratoryId && s.AcademicTermId == dto.AcademicTermId)
        .Where(s => s.DayOfWeek == dto.DayOfWeek)
        .Where(s => excludingClassScheduleId is null || s.Id != excludingClassScheduleId)
        .Any(s => TimeRangesOverlap(dto.TimeStart, dto.TimeEnd, s.TimeStart, s.TimeEnd));
    }

    private static bool TimeRangesOverlap(
    TimeOnly start1,
    TimeOnly end1,
    TimeOnly start2,
    TimeOnly end2)
    {
        return start1 < end2 && start2 < end1;
    }
//update sched
    public async Task UpdateAsync(
        int id,
        UpsertClassScheduleDTO dto)
    {
        await actor.RequirePrivilegeAsync(Privileges.SchedulesManage);
        var schedule = new ClassSchedule
        {
            Id = id,
            CourseId = dto.CourseId,
            InstructorId = dto.InstructorId,
            LaboratoryId = dto.LaboratoryId,
            AcademicTermId = dto.AcademicTermId,
            YearLevel = dto.YearLevel,
            Section = NormalizeSection(dto.Section),
            DayOfWeek = dto.DayOfWeek,
            TimeStart = dto.TimeStart,
            TimeEnd = dto.TimeEnd
        };

        if (await CheckScheduleConflictAsync(dto, excludingClassScheduleId: id))
        {
            throw new InvalidOperationException("The Class Schedule conflicts with an existing class schedule.");
        }

        await _repository.UpdateAsync(schedule);

        // Sync hook: if the edit moved this slot into (or within) a class group, every
        // meeting ends up with the same roster. A slot edited between groups keeps its own
        // students only when the destination group has no roster — see §5.4 of the plan.
        await _classListService.SyncGroupEnrollmentAsync(id);
    }

}