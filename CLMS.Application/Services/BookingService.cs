using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.ValueObjects;

namespace CLMS.Application.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _repository;
    private readonly IClassScheduleRepository _classScheduleRepository;

    public BookingService(
        IBookingRepository repository,
        IClassScheduleRepository classScheduleRepository)
    {
        _repository = repository;
        _classScheduleRepository = classScheduleRepository;
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
            throw new InvalidOperationException("The booking time conflicts with an existing booking or class schedule.");
        }

        await _repository.UpdateAsync(booking);
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