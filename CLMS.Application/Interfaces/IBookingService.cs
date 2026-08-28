using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface IBookingService
{
    Task<int> CreateAsync(UpsertBookingDTO dto);

    Task<IEnumerable<BookingDto>> GetAllAsync();

    Task<BookingDto?> GetByIdAsync(int id);

    Task UpdateAsync(int id, UpsertBookingDTO dto);

    Task DeleteAsync(int id);
}