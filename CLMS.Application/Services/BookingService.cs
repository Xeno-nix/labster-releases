using CLMS.Application.DTOs.Booking;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Booking;
using CLMS.Domain.ValueObjects;

namespace CLMS.Application.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _repository;

    public BookingService(
        IBookingRepository repository)
    {
        _repository = repository;
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

        return await _repository.CreateAsync(booking);
    }

    public async Task<IEnumerable<BookingDto>> GetAllAsync()
    {
        var bookings = await _repository.GetAllAsync();

        return bookings.Select(MapToDto);
    }

    public async Task<BookingDto?> GetByIdAsync(int id)
    {
        var booking = await _repository.GetByIdAsync(id);

        if (booking is null)
            return null;

        return MapToDto(booking);
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

        var booking = await _repository.GetByIdAsync(id);

        if (booking is null)
        {
            throw new KeyNotFoundException(
                $"Booking with ID {id} was not found.");
        }


        booking.LaboratoryId = dto.LaboratoryId;
        booking.RequestorName = dto.RequestorName;
        booking.Email = dto.Email;
        booking.Purpose = dto.Purpose;
        booking.Date = dto.Date;
        booking.StartTime = dto.StartTime;
        booking.EndTime = dto.EndTime;

        await _repository.UpdateAsync(booking);
    }

    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    private static BookingDto MapToDto(
        Booking booking)
    {
        return new BookingDto
        {
            Id = booking.Id,
            LaboratoryId = booking.LaboratoryId,
            RequestorName = booking.RequestorName,
            Email = booking.Email,
            Purpose = booking.Purpose,
            Date = booking.Date,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime
        };
    }
}