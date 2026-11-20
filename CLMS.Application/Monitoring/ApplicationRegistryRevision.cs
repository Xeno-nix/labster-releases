using System.Security.Cryptography;
using System.Text;
using CLMS.Domain.Entities.UnitManagement;

namespace CLMS.Application.Monitoring;

/// <summary>A registry freshness token, unrelated to executable identification. No agent computes it.</summary>
public static class ApplicationRegistryRevision
{
    public static Guid Compute(IEnumerable<MonitoredApplication> applications)
    {
        var text = string.Join('\n', applications.OrderBy(a => a.Id).Select(a => FormattableString.Invariant(
            $"{a.Id:N}|{a.UpdatedAt.Ticks / 10}|{a.IsEnabled}|{a.FlagForReview}|{(int)a.Platform}|{a.ProcessName}")));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }
}
