namespace CLMS.Domain.Entities.Booking;

/// <summary>
/// Links a student to a single weekly meeting of a class.
/// <para>
/// Deliberately does NOT inherit <see cref="Common.AuditableEntity"/>: the
/// student_enrollment table has only enrollment_id, student_id and class_schedule_id —
/// no created_at / last_modified_at / deleted_at. The base class also declares a
/// <c>Guid Id</c>, which collided with this entity's <c>int Id</c> and previously had to be
/// shadowed with <c>new</c>. Dropping the base removes that trap entirely.
/// </para>
/// <para>
/// Because there is no soft-delete column, removing a student from a class is a hard DELETE.
/// </para>
/// </summary>
public class StudentEnrollment
{
    public int Id { get; set; }

    public string StudentId { get; set; } = string.Empty;

    public int ClassScheduleId { get; set; }
}