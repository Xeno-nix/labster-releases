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
