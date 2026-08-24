using System.Collections.Concurrent;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Repositories;
using CLMS.Domain.ValueObjects;

namespace CLMS.Infrastructure.Repositories;

/// <summary>
/// TEMPORARY in-memory implementation. There's no real database wired up yet — this
/// exists so the logging terminal can be built, run, and demoed end-to-end without one.
/// Replace with a real data-access implementation (EF Core or otherwise) once the
/// database is ready. Registered as a singleton so the seeded/added data survives for
/// the lifetime of the app process (see DependencyInjection.cs).
/// </summary>
public class InMemoryStudentRepository : IStudentRepository
{
    private readonly ConcurrentDictionary<string, Student> _students = new();

    public InMemoryStudentRepository()
    {
        // A couple of sample students so the terminal can be manually tested without a DB.
        // Names.Value only allows letters/apostrophes/hyphens (no spaces) — see
        // CLMS.Domain.ValueObjects.Name — so multi-word surnames aren't usable here yet.
        // Remove this seeding once real student data is available.
        Seed(new Student("2024-00123", new Name("Juan"), new Name("Reyes")));
        Seed(new Student("2024-00456", new Name("Maria"), new Name("Santos")));
    }

    private void Seed(Student student) => _students[student.StudentId] = student;

    public Task<Student?> GetByIdAsync(string studentId, CancellationToken cancellationToken = default)
        => Task.FromResult(_students.TryGetValue(studentId, out var student) ? student : null);
}
