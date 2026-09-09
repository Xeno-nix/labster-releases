using CLMS.Domain.Entities.UtilizationLogging;

namespace CLMS.Application.Interfaces;


public interface ILoggingService
{

    Task<LaboratoryUseLog> StudentTimeInAsync(string studentId, DateTime timestamp);
    Task<LaboratoryUseLog> TimeOutAsync(string identifier, DateTime timestamp);
    Task<LaboratoryUseLog> GuestTimeInAsync(string guestName, string bookedUnder, DateTime timestamp);
}
