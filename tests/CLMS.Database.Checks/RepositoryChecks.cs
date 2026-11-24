using CLMS.Application.DTOs.Logbook;
using CLMS.Application.UnitRegistry;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Entities.UnitManagement;
using CLMS.Domain.Entities.Users;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Persistence.MySql;
using CLMS.Infrastructure.Persistence.MySql.Repositories;
using CLMS.Infrastructure.Repositories;
using Dapper;
using MySqlConnector;
using static DatabaseChecks;
using Student = CLMS.Domain.Entities.Users.Student;

internal static class RepositoryChecks
{
    internal static async Task Run(MySqlConnection db, MySqlConnectionFactory factory)
    {
        // Fixtures and all DML live exclusively in the generated disposable database.
        await db.ExecuteAsync("INSERT INTO laboratories(lab_id,lab_name) VALUES(1,'Integration lab')");
        var users = new UserRepository(factory);
        var faculty = new User(Guid.NewGuid(), new Name("Test"), new Name("Faculty"), "faculty@example.test", Role.Faculty, "hash:initial");
        Require(await users.AddAsync(faculty), "user creation returns target affected rows");
        Require((await users.FindByEmailAsync(" FACULTY@example.test "))?.Id == faculty.Id, "normalized email lookup");
        Require((await users.FindAllAsync()).Count == 1 && (await users.FindAllByRoleAsync(Role.Faculty)).Count == 1, "user projections");
        faculty.FirstName = new Name("Updated");
        Require(await users.UpdateAsync(faculty), "profile update");
        Require(await users.IncrementFailedLoginAttemptsAsync(faculty.Id) == 1, "atomic failure count");
        Require(await users.LockOutAsync(faculty.Id, DateTime.UtcNow.AddMinutes(1)), "lockout update");
        Require(await users.ResetFailedLoginAttemptsAsync(faculty.Id), "failure reset");

        var terms = new AcademicTermRepository(factory);
        var term = new AcademicTerm { AcademicYear = "2026-2027", Semester = Semester.First,
            StartDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-30), EndDate = DateOnly.FromDateTime(DateTime.Now).AddDays(30) };
        term.Id = await terms.CreateAsync(term);
        Require(term.Id > 0 && (await terms.GetCurrentAsync(DateOnly.FromDateTime(DateTime.Now)))?.Id == term.Id, "term identity and date mapping through view");
        Require(await terms.UpdateAsync(term) && (await terms.GetAllAsync()).Count() == 1, "term update and list");
        var courses = new CourseRepository(factory);
        var course = new Course { CourseCode = "TEST101", CourseName = "Integration course" };
        course.Id = await courses.CreateAsync(course);
        Require(await courses.UpdateAsync(course) && (await courses.GetByIdAsync(course.Id))?.CourseCode == "TEST101", "course scalar results and view mapping");
        Require((await courses.GetAllAsync()).Count() == 1, "course list");
        var schedules = new ClassScheduleRepository(factory);
        var schedule = new ClassSchedule { CourseId = course.Id, InstructorId = faculty.Id, LaboratoryId = 1, AcademicTermId = term.Id,
            DayOfWeek = DateTime.Now.DayOfWeek, TimeStart = new TimeOnly(0, 0), TimeEnd = new TimeOnly(23, 59), YearLevel = null, Section = null };
        schedule.Id = await schedules.CreateAsync(schedule);
        await schedules.UpdateAsync(schedule);
        Require((await schedules.GetByIdAsync(schedule.Id))?.CourseId == course.Id && (await schedules.GetAllAsync()).Count() == 1, "schedule DTO mapping");
        Require((await schedules.GetByInstructorAsync(faculty.Id, term.Id)).Count() == 1 && (await schedules.GetAllForTermAsync(term.Id)).Count() == 1, "schedule instructor and term filters");
        Require((await schedules.GetGroupScheduleIdsAsync(schedule.Id)).Count == 1 && (await schedules.GetGroupSchedulesAsync(schedule.Id)).Count == 1, "null-safe class grouping");
        try { await courses.DeleteAsync(course.Id); throw new Exception("Referenced course deletion succeeded"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS referenced course deletion rejected"); }
        try { await terms.DeleteAsync(term.Id); throw new Exception("Referenced term deletion succeeded"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS referenced term deletion rejected"); }

        var students = new StudentRepository(factory);
        var student = new Student("TEST-0001", new Name("Jane"), new Name("Student"));
        await students.CreateManyAsync([student]);
        await students.CreateManyAsync([new Student(student.StudentId, new Name("Other"), new Name("Name"))]);
        Require((await students.GetByIdAsync(student.StudentId))?.FirstName.Value == "Jane", "student bulk import preserves existing names");
        Require((await students.FindByIdAsync(student.StudentId))?.FirstName == "Jane" && (await students.FindByIdsAsync([student.StudentId, "missing"])).Count == 1, "student JSON lookup mapping");
        Require((await students.GetExistingIdsAsync([student.StudentId, student.StudentId])).Count == 1 && (await students.GetExistingIdsAsync([])).Count == 0, "student duplicate and empty batches");
        var enrollments = new EnrollmentRepository(factory);
        Require(await enrollments.EnrollAsync([schedule.Id], [student.StudentId]) == 1, "enrollment JSON cross product");
        Require((await enrollments.GetRosterAsync(schedule.Id)).Single().StudentId == student.StudentId && (await enrollments.GetCountsAsync([schedule.Id]))[schedule.Id] == 1, "enrollment roster and count");
        Require((await enrollments.GetEnrolledStudentIdsAsync([schedule.Id])).Single() == student.StudentId, "enrolled ID lookup");
        Require((await schedules.FindCurrentForStudentAsync(student.StudentId, DateTime.Now.DayOfWeek, new TimeOnly(12, 0)))?.Id == schedule.Id, "current enrolled class");
        await using (var tx = await db.BeginTransactionAsync())
        {
            Require(await enrollments.DeleteAllAsync([schedule.Id], db, tx) == 1, "enrollment procedure joins caller transaction");
            Require(await schedules.DeleteManyAsync([schedule.Id], db, tx) == 1, "schedule procedure joins caller transaction");
            await students.CreateManyAsync([new Student("ROLLBACK", new Name("Roll"), new Name("Back"))], db, tx);
            await tx.RollbackAsync();
        }
        Require((await enrollments.GetRosterAsync(schedule.Id)).Count == 1 && await students.FindByIdAsync("ROLLBACK") is null, "bulk operations roll back together");
        Require(await enrollments.RemoveAsync([schedule.Id], student.StudentId) == 1, "remove enrollment");
        await enrollments.EnrollAsync([schedule.Id], [student.StudentId]);

        var labs = new LaboratoryRepository(factory);
        Require((await labs.GetAllAsync()).Count() == 1 && await labs.GetByIdAsync(1) is not null, "laboratory view reads");
        var bookings = new BookingRepository(factory);
        var booking = new Booking(0, 1, "Test visitor", "visitor@example.test", "Testing", DateOnly.FromDateTime(DateTime.Now), new TimeOnly(8, 0), new TimeOnly(9, 0));
        booking.Id = await bookings.CreateAsync(booking);
        await bookings.UpdateAsync(booking);
        Require((await bookings.GetAllAsync()).Count() == 1 && await bookings.GetByIdAsync(booking.Id) is not null &&
            (await bookings.FindByRequestorNameAndDateAsync(booking.RequestorName, booking.Date)).Count() == 1, "booking CRUD and filters");
        await bookings.DeleteAsync(booking.Id);
        Require(!(await bookings.GetAllAsync()).Any(), "booking soft deletion");
        var supplies = new SupplyRepository(factory);
        var supplyId = await supplies.CreateAsync(new Supply(0, "Cable", "Lab", "piece", SupplyStatus.InStock, null));
        await supplies.UpdateAsync(new Supply(supplyId, "Cable", "Lab", "piece", SupplyStatus.LowStock, "Test"));
        await supplies.UpdateStatusAsync(supplyId, SupplyStatus.FullyStocked);
        Require((await supplies.GetByIdAsync(supplyId))?.Status == "fully_stocked" && (await supplies.GetAllAsync()).Count() == 1, "supply enum mapping");
        await supplies.DeleteAsync(supplyId);

        await DeviceAndLogChecks.Run(db, factory, faculty.Id, student.StudentId);
        await IdentityChecks.Run(db, factory);
        await DatabaseObjects.Check(db, FindRoot());
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "CLMS.slnx"))) directory = directory.Parent!;
        return directory.FullName;
    }
}
