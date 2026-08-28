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

        return await _repository.CreateAsync(schedule);
    }
//delete sched
    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

//get all sched
    public async Task<IEnumerable<ClassScheduleDto>> GetAllAsync()
    {
        var schedules = await _repository.GetAllAsync();

        return schedules.Select(MapToDto);}



    public async Task<ClassScheduleDto?> GetByIdAsync(int id)
    {
        var schedule = await _repository.GetByIdAsync(id);

        if (schedule == null)
            return null;

        return MapToDto(schedule);
    }

//update sched
    public async Task UpdateAsync(
        int id,
        UpsertClassScheduleDTO dto)
    {
        var schedule = await _repository.GetByIdAsync(id);

        if (schedule == null)
            throw new KeyNotFoundException(
                $"Class schedule with ID {id} was not found.");

        schedule.CourseId = dto.CourseId;
        schedule.InstructorId = dto.InstructorId;
        schedule.LaboratoryId = dto.LaboratoryId;
        schedule.AcademicTermId = dto.AcademicTermId;
        schedule.DayOfWeek = dto.DayOfWeek;
        schedule.TimeStart = dto.TimeStart;
        schedule.TimeEnd = dto.TimeEnd;

        await _repository.UpdateAsync(schedule);
    }


    private static ClassScheduleDto MapToDto(ClassSchedule schedule)
    {
        return new ClassScheduleDto
        {
            Id = schedule.Id,
            CourseId = schedule.CourseId,
            InstructorId = schedule.InstructorId,
            LaboratoryId = schedule.LaboratoryId,
            AcademicTermId = schedule.AcademicTermId,
            DayOfWeek = schedule.DayOfWeek,
            TimeStart = schedule.TimeStart,
            TimeEnd = schedule.TimeEnd
        };
    }
}