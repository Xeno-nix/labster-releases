using CLMS.Domain.Common;

namespace CLMS.Domain.Entities.UtilizationLogging;
public class UnitUseLog : IUsageLog
{
    public DateOnly LogDate { get; set; }
    public TimeOnly TimeIn { get; set; }
    public TimeOnly TimeOut { get; set; }
    public Guid EquipmentId { get; set; }
}
