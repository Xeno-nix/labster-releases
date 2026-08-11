using CLMS.Domain.Common;

namespace CLMS.Domain.Entities.UtilizationLogging;
public class LaboratoryUseLog : IUsageLog
{
    public DateOnly LogDate { get; set; }
    public TimeOnly TimeIn { get; set; }
    public TimeOnly TimeOut { get; set; }
    public int LaboratoryId { get; set; }
}
