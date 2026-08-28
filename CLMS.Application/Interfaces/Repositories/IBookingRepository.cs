using CLMS.Domain.Entities.Booking;

namespace CLMS.Application.Interfaces.Repositories;

public interface IBookingRepository
{
    Task<int> CreateAsync(Booking booking);

    Task<IEnumerable<Booking>> GetAllAsync();

    Task<Booking?> GetByIdAsync(int id);

    Task UpdateAsync(Booking booking);

    Task DeleteAsync(int id);
}