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

        await _repository.UpdateAsync(booking);
    }

    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

}