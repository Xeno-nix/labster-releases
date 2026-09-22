using CLMS.Application.DTOs.Inventory;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Domain.Entities.Inventory;
using CLMS.Domain.Enums;

namespace CLMS.Application.Services;

public class EquipmentService : IEquipmentService
{
    private readonly IEquipmentRepository _repository;

    public EquipmentService(IEquipmentRepository repository)
    {
        _repository = repository;
    }

    // ---------------------------------------------------------------
    // Equipment CRUD
    // ---------------------------------------------------------------

    public async Task<Guid> CreateAsync(UpsertEquipmentDTO dto)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var equipmentNo = await _repository.GetNextEquipmentNoAsync();

            var equipment = new Equipment(
                Guid.NewGuid(),
                equipmentNo,
                dto.SerialNumber,
                dto.EquipmentName,
                dto.Brand,
                dto.ModelNumber,
                dto.EquipmentType,
                ParseStatus(dto.Status),
                dto.NeedsPm,
                dto.NeedsCalibration,
                dto.Remarks);

            try
            {
                await _repository.CreateAsync(equipment);
                return equipment.Id;
            }
            catch (InvalidOperationException ex) when (ex.Message == "EQUIPMENT_NO_CONFLICT" && attempt < maxAttempts)
            {
                // Someone else's save grabbed this number first — retry with a fresh one.
            }
        }

        throw new InvalidOperationException("Could not generate a unique equipment number. Please try again.");
    }
    public Task<IEnumerable<EquipmentDTO>> GetAllAsync()
        => _repository.GetAllAsync();

    public Task<EquipmentDTO?> GetByIdAsync(Guid id)
        => _repository.GetByIdAsync(id);

    public async Task UpdateAsync(Guid id, UpsertEquipmentDTO dto)
    {
        var existing = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Equipment not found.");

        var equipment = new Equipment(
            id,
            existing.EquipmentNo,
            dto.SerialNumber,
            dto.EquipmentName,
            dto.Brand,
            dto.ModelNumber,
            dto.EquipmentType,
            ParseStatus(dto.Status),
            dto.NeedsPm,
            dto.NeedsCalibration,
            dto.Remarks);

        await _repository.UpdateAsync(equipment);
    }

    public Task DeleteAsync(Guid id)
        => _repository.DeleteAsync(id);

    // ---------------------------------------------------------------
    // Assignment orchestration
    // ---------------------------------------------------------------

    public async Task<Guid> AssignAsync(AssignEquipmentDTO dto)
    {
        if (dto.ComputerUnitId is null == dto.LaboratoryId is null)
        {
            throw new ArgumentException(
                "Exactly one of ComputerUnitId or LaboratoryId must be provided.");
        }

        var existingActive = await _repository.GetActiveAssignmentAsync(dto.EquipmentId);
        if (existingActive is not null)
        {
            throw new InvalidOperationException(
                "This equipment already has an active assignment. End it before assigning it elsewhere.");
        }

        var assignment = new EquipmentAssignment(
            Guid.NewGuid(),
            dto.EquipmentId,
            dto.ComputerUnitId,
            dto.LaboratoryId,
            DateTime.UtcNow);

        await _repository.CreateAssignmentAsync(assignment);

        return assignment.Id;
    }

    public async Task EndAssignmentAsync(Guid assignmentId, EndEquipmentAssignmentDTO dto)
    {
        var assignment = await _repository.GetAssignmentByIdAsync(assignmentId)
            ?? throw new InvalidOperationException("Assignment not found.");

        if (assignment.Status == AssignmentStatus.Ended)
        {
            throw new InvalidOperationException("This assignment has already ended.");
        }

        assignment.End(ParseEndReason(dto.EndReason), dto.Remarks, DateTime.UtcNow);

        await _repository.EndAssignmentAsync(assignment);
    }

    public async Task EndCurrentAssignmentAsync(Guid equipmentId, EndEquipmentAssignmentDTO dto)
    {
        var assignment = await _repository.GetActiveAssignmentAsync(equipmentId)
            ?? throw new InvalidOperationException("This equipment has no active assignment to end.");

        assignment.End(ParseEndReason(dto.EndReason), dto.Remarks, DateTime.UtcNow);

        await _repository.EndAssignmentAsync(assignment);
    }

    public Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentHistoryAsync(Guid equipmentId)
        => _repository.GetAssignmentHistoryAsync(equipmentId);

    public Task<IEnumerable<EquipmentAssignmentDTO>> GetAssignmentsForComputerUnitAsync(Guid computerUnitId)
        => _repository.GetAssignmentsForComputerUnitAsync(computerUnitId);

    // ---------------------------------------------------------------
    // String <-> enum parsing (DTOs carry plain strings; the domain uses enums)
    // ---------------------------------------------------------------

    private static EquipmentStatus ParseStatus(string value)
    {
        var normalized = value.Replace(" ", "").Replace("_", "");

        if (Enum.TryParse<EquipmentStatus>(normalized, ignoreCase: true, out var status))
            return status;

        throw new ArgumentException($"Unrecognized equipment status: '{value}'.");
    }

    private static AssignmentEndReason ParseEndReason(string value)
    {
        if (Enum.TryParse<AssignmentEndReason>(value, ignoreCase: true, out var reason))
            return reason;

        throw new ArgumentException($"Unrecognized end reason: '{value}'.");
    }
}
