using CLMS.Shared.Dtos;

namespace CLMS.Application.UnitRegistry;

public interface IAgentTokenService
{
    string Audience { get; }
    /// <summary>Issues access-token metadata for an approved unit and its active persistent credential.</summary>
    AgentTokens Issue(Guid unitId, int generation, Guid credentialId, DateTime now);
    /// <summary>Creates a cryptographically random private registration secret.</summary>
    string NewSecret();
    /// <summary>Creates a human-readable registration verification code; this code must not authenticate credential claims.</summary>
    string NewCode();
}
