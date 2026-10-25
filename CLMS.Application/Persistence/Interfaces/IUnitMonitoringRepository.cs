using CLMS.Application.DTOs.Monitoring;

namespace CLMS.Application.Interfaces.Repositories;

public interface IUnitMonitoringRepository
{
    Task<IReadOnlyList<UnitMonitoringDTO>> GetAllAsync(CancellationToken cancellationToken = default);
}
