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
    public async Task<IEnumerable<ClassScheduleDTO>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }



    public async Task<ClassScheduleDTO?> GetByIdAsync(int id)
    {
        return  await _repository.GetByIdAsync(id);
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

        await _repository.UpdateAsync(schedule);
    }

}