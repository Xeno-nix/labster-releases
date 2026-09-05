using CLMS.Application.DTOs.Booking;

namespace CLMS.Application.Interfaces;

public interface IBookingService
{
    Task<int> CreateAsync(UpsertBookingDTO dto);

    Task<IEnumerable<BookingDTO>> GetAllAsync();

    Task<BookingDTO?> GetByIdAsync(int id);

    Task UpdateAsync(int id, UpsertBookingDTO dto);

    Task DeleteAsync(int id);
    Task<bool> CheckScheduleConflictAsync(UpsertBookingDTO dto, int? excludingBookingId = null);
}