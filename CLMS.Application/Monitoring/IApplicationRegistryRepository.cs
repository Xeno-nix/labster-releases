using CLMS.Domain.Entities.UnitManagement;

namespace CLMS.Application.Monitoring;

public interface IApplicationRegistryRepository
{
    Task<IReadOnlyList<MonitoredApplication>> GetAllAsync(CancellationToken ct = default);
    Task SaveAsync(MonitoredApplication application, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IApplicationRegistryNotifications
{
    Task RegistryChangedAsync(CancellationToken ct = default);
}
