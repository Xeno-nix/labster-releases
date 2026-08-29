using CLMS.Application.Inventory;

namespace CLMS.Infrastructure.Services;

/// <summary>
/// Temporary in-memory implementation of IInventoryService.
/// Use this while the real database schema/connection isn't ready yet.
/// Data lives only in process memory and resets whenever the app restarts.
///
/// To switch back to the real MySQL-backed service later, go to
/// CLMS.Infrastructure/DependencyInjection.cs and register InventoryService
/// instead of InMemoryInventoryService.
/// </summary>
public sealed class InMemoryInventoryService : IInventoryService
{
    // Registered as a Singleton (see DependencyInjection.cs) so the data
    // survives page navigation / reconnects for the lifetime of the app process.
    private readonly object _lock = new();
    private readonly List<InventoryEquipmentDto> _equipment;
    private readonly List<InventorySupplyDto> _supplies;
    private readonly List<InventoryLabDto> _labs;
    private int _nextEquipmentId;
    private int _nextSupplyId;

    public InMemoryInventoryService()
    {
        _labs = new List<InventoryLabDto>
        {
            new(1, "Lab 1"),
            new(2, "Lab 2"),
            new(3, "Lab 3"),
        };

        _equipment = new List<InventoryEquipmentDto>
        {
            new(1, "EQ-001", "SN-A001", "Desktop PC", "Dell", "OptiPlex 7090", "computer_unit", 1, "Lab 1", "available", 1, true, false, null),
            new(2, "EQ-002", "SN-A002", "Desktop PC", "Dell", "OptiPlex 7090", "computer_unit", 1, "Lab 1", "in-use", 1, true, false, null),
            new(3, "EQ-003", "SN-A003", "Projector", "Epson", "EB-X06", "peripheral", 1, "Lab 1", "available", 1, false, true, null),
            new(4, "EQ-004", "SN-B001", "Desktop PC", "HP", "ProDesk 400", "computer_unit", 2, "Lab 2", "available", 1, true, false, null),
            new(5, "EQ-005", "SN-B002", "Network Switch", "Cisco", "Catalyst 2960", "component", 2, "Lab 2", "available", 1, true, true, null),
            new(6, "EQ-006", "SN-B003", "Printer", "Brother", "HL-L2350DW", "peripheral", 2, "Lab 2", "maintenance", 1, false, false, null),
            new(7, "EQ-007", "SN-C001", "Desktop PC", "Lenovo", "ThinkCentre M70", "computer_unit", 3, "Lab 3", "damaged", 1, false, false, "Needs replacement part"),
            new(8, "EQ-008", "SN-A004", "Wireless Mouse", "Logitech", "M185", "peripheral", 1, "Lab 1", "available", 1, false, false, null),
            new(9, "ST-093", "BN-3000282", "Laptop", "opus", "cyberEx2026v3", "computer_unit", 1, "Lab 1", "damaged", 1, false, false, "Cracked screen"),
        };
        _nextEquipmentId = _equipment.Count + 1;

        _supplies = new List<InventorySupplyDto>
        {
            new(1, "Bond Paper (A4)", "Office Supplies", "ream", "in_stock", "For printers"),
            new(2, "Ink Cartridge (Black)", "Printing Supplies", "piece", "low_stock", "Reorder soon"),
            new(3, "Whiteboard Marker", "Office Supplies", "piece", "fully_stocked", null),
            new(4, "Isopropyl Alcohol", "Cleaning Supplies", "bottle", "in_stock", "For sanitizing peripherals"),
            new(5, "Microfiber Cloth", "Cleaning Supplies", "piece", "fully_stocked", null),
            new(6, "HDMI Cable", "Cables & Connectors", "piece", "low_stock", null),
            new(7, "USB Flash Drive", "Other", "piece", "out_of_stock", "Awaiting restock"),
            new(8, "Screwdriver Set", "Tools", "set", "fully_stocked", "Shared toolbox, Lab 2"),
        };
        _nextSupplyId = _supplies.Count + 1;
    }

    public Task<IReadOnlyList<InventoryEquipmentDto>> GetEquipmentAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<InventoryEquipmentDto>>(_equipment.ToList());
        }
    }

    public Task<IReadOnlyList<InventorySupplyDto>> GetSuppliesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<InventorySupplyDto>>(_supplies.ToList());
        }
    }

    public Task<IReadOnlyList<InventoryLabDto>> GetLabsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<InventoryLabDto>>(_labs.ToList());
        }
    }

    public Task<InventoryEquipmentDto?> GetEquipmentByIdAsync(int equipmentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_equipment.FirstOrDefault(e => e.EquipmentId == equipmentId));
        }
    }

    public Task CreateOrUpdateEquipmentAsync(InventoryEquipmentDto dto, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var labName = _labs.FirstOrDefault(l => l.LabId == dto.LabId)?.LabName ?? dto.LabName;

            if (dto.EquipmentId > 0)
            {
                var idx = _equipment.FindIndex(e => e.EquipmentId == dto.EquipmentId);
                if (idx >= 0)
                {
                    _equipment[idx] = dto with { LabName = labName };
                    return Task.CompletedTask;
                }
            }

            var newId = _nextEquipmentId++;
            _equipment.Add(dto with { EquipmentId = newId, LabName = labName });
            return Task.CompletedTask;
        }
    }

    public Task DeleteEquipmentAsync(int equipmentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _equipment.RemoveAll(e => e.EquipmentId == equipmentId);
            return Task.CompletedTask;
        }
    }

    public Task<InventorySupplyDto?> GetSupplyByIdAsync(int supplyId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_supplies.FirstOrDefault(s => s.SupplyId == supplyId));
        }
    }

    public Task CreateOrUpdateSupplyAsync(InventorySupplyDto dto, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (dto.SupplyId > 0)
            {
                var idx = _supplies.FindIndex(s => s.SupplyId == dto.SupplyId);
                if (idx >= 0)
                {
                    _supplies[idx] = dto;
                    return Task.CompletedTask;
                }
            }

            var newId = _nextSupplyId++;
            _supplies.Add(dto with { SupplyId = newId });
            return Task.CompletedTask;
        }
    }

    public Task DeleteSupplyAsync(int supplyId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _supplies.RemoveAll(s => s.SupplyId == supplyId);
            return Task.CompletedTask;
        }
    }

    public Task UpdateSupplyStatusAsync(int supplyId, string status, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idx = _supplies.FindIndex(s => s.SupplyId == supplyId);
            if (idx >= 0)
            {
                _supplies[idx] = _supplies[idx] with { Status = status };
            }
            return Task.CompletedTask;
        }
    }
}