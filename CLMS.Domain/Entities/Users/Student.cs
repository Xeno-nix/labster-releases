using System;
using System.Collections.Generic;
using CLMS.Domain.Common;
using CLMS.Domain.ValueObjects;

namespace CLMS.Domain.Entities.Users;

public class Student : AuditableEntity
{
    public string StudentId { get; private set; }
    public Name FirstName { get; private set; }
    public Name? MiddleName { get; private set; }
    public Name LastName { get; private set; }
    public string? Suffix { get; private set; }
    public string? Email { get; private set; }
    public ContactNumber? ContactNumber { get; private set; }

    // navigation
    public IReadOnlyCollection<Booking.StudentEnrollment> Enrollments => _enrollments.AsReadOnly();
    private readonly List<Booking.StudentEnrollment> _enrollments = new();

    private Student()
    {
        // EF Core materialization only.
        StudentId = string.Empty;
        FirstName = null!;
        LastName = null!;
    }

    public Student(string studentId, Name firstName, Name lastName)
    {
        if (string.IsNullOrWhiteSpace(studentId)) throw new ArgumentException("studentId is required", nameof(studentId));
        StudentId = studentId;
        FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
        LastName = lastName ?? throw new ArgumentNullException(nameof(lastName));
        _id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
    }

    public void SetMiddleName(Name middle) => MiddleName = middle;
    public void SetSuffix(string? suffix) => Suffix = suffix;
    public void SetEmail(string? email) => Email = email;
    public void SetContactNumber(ContactNumber? contact) => ContactNumber = contact;

    public void AddEnrollment(Booking.StudentEnrollment enrollment)
    {
        if (enrollment == null) throw new ArgumentNullException(nameof(enrollment));
        // Prevent duplicate enrollment to same class schedule
        if (_enrollments.Exists(e => e.ClassScheduleId == enrollment.ClassScheduleId))
            throw new InvalidOperationException($"Student is already enrolled in class schedule {enrollment.ClassScheduleId}");

        _enrollments.Add(enrollment);
    }

    /// <summary>
    /// Returns true if the student has an active scheduled class at the given timestamp.
    /// The resolver callback is used to obtain the ClassSchedule for an enrollment id.
    /// </summary>
    public bool HasActiveScheduleAt(DateTime timestamp, Func<int, Booking.ClassSchedule?> classScheduleResolver)
    {
        if (classScheduleResolver == null) throw new ArgumentNullException(nameof(classScheduleResolver));

        var day = timestamp.DayOfWeek;
        var time = TimeOnly.FromDateTime(timestamp);

        foreach (var enrollment in _enrollments)
        {
            var schedule = classScheduleResolver(enrollment.ClassScheduleId);
            if (schedule?.Schedules == null) continue;

            foreach (var s in schedule.Schedules)
            {
                if (s.DayOfWeek != day) continue;
                if (time >= s.TimeStart && time <= s.TimeEnd)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the ClassSchedule that matches the current timestamp, or null if none.
    /// </summary>
    public Booking.ClassSchedule? GetCurrentClassSchedule(DateTime timestamp, Func<int, Booking.ClassSchedule?> classScheduleResolver)
    {
        if (classScheduleResolver == null) throw new ArgumentNullException(nameof(classScheduleResolver));

        var day = timestamp.DayOfWeek;
        var time = TimeOnly.FromDateTime(timestamp);

        foreach (var enrollment in _enrollments)
        {
            var schedule = classScheduleResolver(enrollment.ClassScheduleId);
            if (schedule?.Schedules == null) continue;

            foreach (var s in schedule.Schedules)
            {
                if (s.DayOfWeek != day) continue;
                if (time >= s.TimeStart && time <= s.TimeEnd)
                    return schedule;
            }
        }

        return null;
    }
}
