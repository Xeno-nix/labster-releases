using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;


namespace CLMS.Application.Services;

public class CourseService 
{
    private readonly ICurrentUser actor;
    private readonly ICourseRepository _repository;

    public CourseService(
        ICourseRepository repository, ICurrentUser actor)
    {
        this.actor = actor;
        _repository = repository;
    }

    public async Task<IEnumerable<CourseDTO>> GetAllAsync()
    {
        await actor.RequirePrivilegeAsync(Privileges.CoursesView);
        var courses = await _repository.GetAllAsync();

        return courses.Select(MapToDto);
    }

    public async Task<int> CreateAsync(CreateCourseDTO model)
    {
        await actor.RequirePrivilegeAsync(Privileges.CoursesManage);
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(model, new(model), true);
        var code = model.CourseCode.Trim();
        if ((await _repository.GetAllAsync()).Any(c => string.Equals(c.CourseCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A course with this code already exists.");

        return await _repository.CreateAsync(new Course
        {
            CourseCode = code,
            CourseName = model.CourseName.Trim()
        });
    }

    public async Task UpdateAsync(int id, CreateCourseDTO model)
    {
        await actor.RequirePrivilegeAsync(Privileges.CoursesManage);
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(model, new(model), true);
        var code = model.CourseCode.Trim();
        if ((await _repository.GetAllAsync()).Any(c => c.Id != id && string.Equals(c.CourseCode.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A course with this code already exists.");

        if (!await _repository.UpdateAsync(new Course { Id = id, CourseCode = code, CourseName = model.CourseName.Trim() }))
            throw new InvalidOperationException("This course no longer exists. Refresh the list and try again.");
    }

    public async Task DeleteAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.CoursesManage);
        if (!await _repository.DeleteAsync(id))
            throw new InvalidOperationException("This course no longer exists. Refresh the list and try again.");
    }

    public async Task<CourseDTO?> GetByIdAsync(int id)
    {
        await actor.RequirePrivilegeAsync(Privileges.CoursesView);
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
