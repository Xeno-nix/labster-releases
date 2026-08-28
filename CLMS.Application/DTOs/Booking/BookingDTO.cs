using CLMS.Domain.ValueObjects;

namespace CLMS.Application.DTOs.Booking;

public class BookingDto
{
    public int Id { get; set; }

    public int LaboratoryId { get; set; }

    public string RequestorName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}