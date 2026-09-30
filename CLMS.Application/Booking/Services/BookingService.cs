using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.ValueObjects;

namespace CLMS.Application.Services;

public class BookingService
{
    private readonly IBookingRepository _repository;
    private readonly IClassScheduleRepository _classScheduleRepository;
    private readonly IEmailService _emailService;
    private readonly ILaboratoryRepository _laboratoryRepository;

    public BookingService(
        IBookingRepository repository,
        IClassScheduleRepository classScheduleRepository,
        IEmailService emailService,
        ILaboratoryRepository laboratoryRepository)
    {
        _emailService = emailService;
        _repository = repository;
        _classScheduleRepository = classScheduleRepository;
        _laboratoryRepository = laboratoryRepository;
    }

    public async Task<int> CreateAsync(UpsertBookingDTO dto)
    {
        if (dto.StartTime >= dto.EndTime)
        {
            throw new ArgumentException(
                "Start time must be earlier than end time.");
        }

        var booking = new Booking
        {
            LaboratoryId = dto.LaboratoryId,
            RequestorName = dto.RequestorName,
            Email = dto.Email,
            Purpose = dto.Purpose,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime
        };

        if (await CheckScheduleConflictAsync(dto))
        {
            throw new InvalidOperationException("The booking time conflicts with an existing booking or class schedule.");
        }

        var laboratory = await _laboratoryRepository.GetByIdAsync(dto.LaboratoryId);
    
        await _emailService.SendEmailAsync(
            dto.Email,
            "CLMS - Booking Confirmation",
            $"""
            <html>
            <body>
                <h2>Laboratory Booking Confirmed</h2>

                <p>Hello <strong>{booking.RequestorName}</strong>,</p>

                <p>
                    Your laboratory booking has been successfully created.
                    Here is a summary of your booking:
                </p>

                <table style="border-collapse: collapse; width: 100%; max-width: 600px;">
                    <tr>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            <strong>Laboratory</strong>
                        </td>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            Laboratory ID: {laboratory.Name}
                        </td>
                    </tr>

                    <tr>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            <strong>Requestor</strong>
                        </td>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            {booking.RequestorName}
                        </td>
                    </tr>

                    <tr>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            <strong>Date</strong>
                        </td>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            {booking.Date:MMMM dd, yyyy}
                        </td>
                    </tr>

                    <tr>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            <strong>Time</strong>
                        </td>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            {booking.StartTime:hh:mm tt}
                            -
                            {booking.EndTime:hh:mm tt}
                        </td>
                    </tr>

                    <tr>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            <strong>Purpose</strong>
                        </td>
                        <td style="padding: 8px; border: 1px solid #ddd;">
                            {booking.Purpose}
                        </td>
                    </tr>
                </table>

                <p>
                    Please keep this email for your records.
                </p>

                <p>
                    Thank you,<br>
                    <strong>CLMS Laboratory Management System</strong>
                </p>
            </body>
            </html>
            """
        );
        return await _repository.CreateAsync(booking);
        
    }

    public async Task<IEnumerable<BookingDTO>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<BookingDTO?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task UpdateAsync(
    int id,
    UpsertBookingDTO dto)
{
    if (dto.StartTime >= dto.EndTime)
    {
        throw new ArgumentException(
            "Start time must be earlier than end time.");
    }

    var booking = new Booking
    {
        Id = id,
        LaboratoryId = dto.LaboratoryId,
        RequestorName = dto.RequestorName,
        Email = dto.Email,
        Purpose = dto.Purpose,
        Date = dto.Date,
        StartTime = dto.StartTime,
        EndTime = dto.EndTime
    };

    if (await CheckScheduleConflictAsync(dto, excludingBookingId: id))
    {
        throw new InvalidOperationException(
            "The booking time conflicts with an existing booking or class schedule.");
    }

    var laboratory = await _laboratoryRepository.GetByIdAsync(dto.LaboratoryId);

    await _repository.UpdateAsync(booking);

    await _emailService.SendEmailAsync(
        dto.Email,
        "CLMS - Booking Updated",
        $"""
        <html>
        <body>
            <h2>Laboratory Booking Updated</h2>

            <p>Hello <strong>{booking.RequestorName}</strong>,</p>

            <p>
                Your laboratory booking has been successfully updated.
                Here is the updated summary of your booking:
            </p>

            <table style="border-collapse: collapse; width: 100%; max-width: 600px;">
                <tr>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        <strong>Laboratory</strong>
                    </td>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        {laboratory.Name}
                    </td>
                </tr>

                <tr>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        <strong>Requestor</strong>
                    </td>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        {booking.RequestorName}
                    </td>
                </tr>

                <tr>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        <strong>Date</strong>
                    </td>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        {booking.Date:MMMM dd, yyyy}
                    </td>
                </tr>

                <tr>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        <strong>Time</strong>
                    </td>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        {booking.StartTime:hh:mm tt}
                        -
                        {booking.EndTime:hh:mm tt}
                    </td>
                </tr>

                <tr>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        <strong>Purpose</strong>
                    </td>
                    <td style="padding: 8px; border: 1px solid #ddd;">
                        {booking.Purpose}
                    </td>
                </tr>
            </table>

            <p>
                Please keep this email for your records.
            </p>

            <p>
                Thank you,<br>
                <strong>CLMS Laboratory Management System</strong>
            </p>
        </body>
        </html>
        """
    );
}

    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task<bool> CheckScheduleConflictAsync(UpsertBookingDTO dto, int? excludingBookingId = null)
    {
        var allBookings = await _repository.GetAllAsync();

        var bookingConflict = allBookings
            .Where(b => b.LaboratoryId == dto.LaboratoryId && b.Date == dto.Date)
            .Where(b => excludingBookingId is null || b.Id != excludingBookingId)
            .Any(b => TimeRangesOverlap(dto.StartTime, dto.EndTime, b.StartTime, b.EndTime));

        if (bookingConflict)
        {
            return true;
        }

        var allSchedules = await _classScheduleRepository.GetAllAsync();
        var targetDayOfWeek = dto.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek;

        return allSchedules
            .Where(s => s.LaboratoryId == dto.LaboratoryId)
            .Where(s => s.DayOfWeek == targetDayOfWeek)
            .Any(s => TimeRangesOverlap(dto.StartTime, dto.EndTime, s.TimeStart, s.TimeEnd));
    }


    private static bool TimeRangesOverlap(
        TimeOnly start1,
        TimeOnly end1,
        TimeOnly start2,
        TimeOnly end2)
    {
        return start1 < end2 && start2 < end1;
    }

}