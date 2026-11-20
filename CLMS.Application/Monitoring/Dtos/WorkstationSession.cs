namespace CLMS.Application.DTOs.Monitoring;

public enum WorkstationSessionWrite { Accepted, Conflict, NotFound, Ended }

public sealed record WorkstationSession(Guid SessionId, Guid UnitId, string UnitName, string UserName,
    DateTime StartedUtc, DateTime? EndedUtc);
