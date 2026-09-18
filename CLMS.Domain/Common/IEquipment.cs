namespace CLMS.Domain.Common;

public interface IEquipment
{
    string? SerialNumber { get; set; }
    string EquipmentName { get; set; }
}
