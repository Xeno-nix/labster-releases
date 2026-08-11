namespace CLMS.Domain.Entities.Booking;
public class Schedule
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly TimeStart { get; set; }
    public TimeOnly TimeEnd { get; set; }
}
