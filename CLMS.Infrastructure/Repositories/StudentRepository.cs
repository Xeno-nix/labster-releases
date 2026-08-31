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
        // Sample students covering the main terminal test scenarios. Names.Value only
        // allows letters/apostrophes/hyphens (no spaces) — see CLMS.Domain.ValueObjects.Name
        // — so multi-word surnames aren't usable here yet.
        //
        //   2024-00123  Juan Reyes    — no active session; use to test a fresh Time-In
        //   2024-00456  Maria Santos  — pre-seeded WITH an active session by
        //                                InMemoryLabUtilizationRepository; use to test
        //                                "already has an active session" on Time-In, and
        //                                a normal Time-Out
        //   2024-00789  Pedro Cruz    — no active session; a second "fresh" student
        //   0000-00000  (intentionally absent) — use any unseeded id to test "student not found"
        //
        // Remove this seeding once real student data is available.
        Seed(new Student("2024-00123", new Name("Juan"), new Name("Reyes")));
        Seed(new Student("2024-00456", new Name("Maria"), new Name("Santos")));
        Seed(new Student("2024-00789", new Name("Pedro"), new Name("Cruz")));
    }

    private void Seed(Student student) => _students[student.StudentId] = student;

    public Task<Student?> GetByIdAsync(string studentId, CancellationToken cancellationToken = default)
        => Task.FromResult(_students.TryGetValue(studentId, out var student) ? student : null);

    // ── TEMP-DEBUG ─────────────────────────────────────────────────
    // Not part of IStudentRepository — only used by the temporary browser-console logger
    // (see CLMS.Web/Debug/TerminalDebugLogger.cs) to dump current state for testing.
    // Delete this method when that debug logger is removed.
    public IReadOnlyList<Student> GetAllForDebug() => _students.Values.ToList();
}
