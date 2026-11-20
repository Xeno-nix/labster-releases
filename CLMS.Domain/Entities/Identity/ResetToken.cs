namespace CLMS.Domain.Entities.Identity;

public class ResetToken
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    /// <summary>Address snapshot for conditional issuance; this adds no database column.</summary>
    public string Email { get; init; } = "";

    public ResetToken(Guid userId, string tokenHash, DateTime expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }
}
