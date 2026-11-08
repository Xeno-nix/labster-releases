namespace CLMS.Agent.Core;

/// <summary>A nonsecret observation from the graphical-session companion, never an authentication proof.</summary>
public sealed record MacAccessEvent(Guid Id, string Kind, Guid BootId, int AuditSessionId, DateTimeOffset ObservedUtc);
