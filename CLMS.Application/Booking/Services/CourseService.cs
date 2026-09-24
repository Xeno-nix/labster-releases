using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;


namespace CLMS.Application.Services;

public class CourseService 
{
    private readonly ICourseRepository _repository;

    public CourseService(
        ICourseRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<CourseDTO>> GetAllAsync()
    {
        var courses = await _repository.GetAllAsync();

        return courses.Select(MapToDto);
    }

    public async Task<CourseDTO?> GetByIdAsync(int id)
    {
        var course = await _repository.GetByIdAsync(id);

        if (course is null)
            return null;

        return MapToDto(course);
    }


    private static CourseDTO MapToDto(
        Course course)
    {
        return new CourseDTO
        {
            Id = course.Id,
            CourseCode = course.CourseCode,
            CourseName = course.CourseName
        };
    }
}