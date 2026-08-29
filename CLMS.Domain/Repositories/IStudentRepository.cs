using CLMS.Domain.Entities.Users;

namespace CLMS.Domain.Repositories;

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(string studentId, CancellationToken cancellationToken = default);
}
