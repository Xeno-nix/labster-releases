using CLMS.Domain.Entities.Users;

namespace CLMS.Application.Interfaces.Repositories;

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(string studentId);
}
