namespace CLMS.Domain.Entities.Booking;
public class Course
{
    public readonly int Id;
    public readonly string CourseCode;
    public string CourseName { get; set; }

    public Course(int id, string courseCode, string courseName)
    {
        Id = id;
        CourseCode = courseCode;
        CourseName = courseName;
    }

}

