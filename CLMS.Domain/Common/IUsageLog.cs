namespace CLMS.Domain.Common;
public interface IUsageLog
{
    public DateOnly LogDate { get; set; }
    public TimeOnly TimeIn { get; set; }
    public TimeOnly TimeOut { get; set; }

}
