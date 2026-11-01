using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;


namespace CLMS.Application.Services;

public class AcademicTermService
{
    private readonly IAcademicTermRepository _repository;

    public AcademicTermService(
        IAcademicTermRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<AcademicTermDTO>> GetAllAsync()
    {
        var academicTerms = await _repository.GetAllAsync();

        return academicTerms.Select(MapToDto);
    }

    public async Task<int> CreateAsync(CreateAcademicTermDTO model)
    {
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(model, new(model), true);
        if ((await _repository.GetAllAsync()).Any(t => t.AcademicYear == model.AcademicYear && t.Semester == model.Semester))
            throw new InvalidOperationException("This semester already exists for the selected school year.");

        return await _repository.CreateAsync(new AcademicTerm
        {
            AcademicYear = model.AcademicYear,
            Semester = model.Semester!.Value,
            StartDate = model.StartDate,
            EndDate = model.EndDate
        });
    }

    public async Task UpdateAsync(int id, CreateAcademicTermDTO model)
    {
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(model, new(model), true);
        if ((await _repository.GetAllAsync()).Any(t => t.Id != id && t.AcademicYear == model.AcademicYear && t.Semester == model.Semester))
            throw new InvalidOperationException("This semester already exists for the selected school year.");

        if (!await _repository.UpdateAsync(new AcademicTerm
        {
            Id = id,
            AcademicYear = model.AcademicYear,
            Semester = model.Semester!.Value,
            StartDate = model.StartDate,
            EndDate = model.EndDate
        }))
            throw new InvalidOperationException("This school semester no longer exists. Refresh the list and try again.");
    }

    public async Task DeleteAsync(int id)
    {
        if (!await _repository.DeleteAsync(id))
            throw new InvalidOperationException("This school semester no longer exists. Refresh the list and try again.");
    }

    private static AcademicTermDTO MapToDto(
        AcademicTerm academicTerm)
    {
        return new AcademicTermDTO
        {
            Id = academicTerm.Id,
            AcademicYear = academicTerm.AcademicYear,
            Semester = academicTerm.Semester.ToString(),
            StartDate = academicTerm.StartDate,
            EndDate = academicTerm.EndDate
        };
    }
}
