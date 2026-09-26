using CLMS.Application.DTOs.Booking;
using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface IBookingRepository
{
    Task<int> CreateAsync(Booking booking);

    Task<IEnumerable<BookingDTO>> GetAllAsync();

    Task<BookingDTO?> GetByIdAsync(int id);

    /// <summary>
    /// Bookings made under the given requestor name ("booked under") for a given date.
    /// Added for the logging terminal's Guest Time-In flow — see BookingRepository for
    /// the query. Note: the current `bookings` schema has no approval-status column, so
    /// this returns every booking for that name/date regardless of any such concept.
    /// </summary>
    Task<IEnumerable<Booking>> FindByRequestorNameAndDateAsync(string requestorName, DateOnly date);

    Task UpdateAsync(Booking booking);

    Task DeleteAsync(int id);
}