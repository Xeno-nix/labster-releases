using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Services;

public class ClassScheduleService : IClassScheduleService
{
    private readonly IClassScheduleRepository _repository;
    

    public ClassScheduleService(
        IClassScheduleRepository repository)
    {
        _repository = repository;
    }

//create sched
    public async Task<int> CreateAsync(UpsertClassScheduleDTO dto)
    {
        var schedule = new ClassSchedule
        {
            CourseId = dto.CourseId,
            InstructorId = dto.InstructorId,
            LaboratoryId = dto.LaboratoryId,
            AcademicTermId = dto.AcademicTermId,
            DayOfWeek = dto.DayOfWeek,
            TimeStart = dto.TimeStart,
            TimeEnd = dto.TimeEnd
        };

        if (await CheckScheduleConflictAsync(dto))
        {
            throw new InvalidOperationException("The Class Schedule conflicts with an existing class schedule.");
        }

        return await _repository.CreateAsync(schedule);
    }
//delete sched
    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

//get all sched
    public async Task<IEnumerable<ClassScheduleDTO>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }



    public async Task<ClassScheduleDTO?> GetByIdAsync(int id)
    {
        return  await _repository.GetByIdAsync(id);
    }

    public async Task<bool> CheckScheduleConflictAsync(UpsertClassScheduleDTO dto, int? excludingClassScheduleId = null)
    {
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
        var schedule = new ClassSchedule
        {
            Id = id,
            CourseId = dto.CourseId,
            InstructorId = dto.InstructorId,
            LaboratoryId = dto.LaboratoryId,
            AcademicTermId = dto.AcademicTermId,
            DayOfWeek = dto.DayOfWeek,
            TimeStart = dto.TimeStart,
            TimeEnd = dto.TimeEnd
        };

        if (await CheckScheduleConflictAsync(dto, excludingClassScheduleId: id))
        {
            throw new InvalidOperationException("The Class Schedule conflicts with an existing class schedule.");
        }

        await _repository.UpdateAsync(schedule);
    }

}