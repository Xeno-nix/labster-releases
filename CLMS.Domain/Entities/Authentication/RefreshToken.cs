namespace CLMS.Domain.Entities.Authentication;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired =>
        DateTime.UtcNow >= ExpiresAt;
    public bool IsActive =>
        !IsRevoked && !IsExpired;

    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        string tokenHash,
        DateTime createdAt,
        DateTime expiresAt)
    {
        Id = id;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public void Revoke()
    {
        if (IsRevoked)
            return;

        RevokedAt = DateTime.UtcNow;
    }

    public static RefreshToken Create(Guid id,
                                      Guid userId,
                                      string tokenHash,
                                      DateTime createdAt,
                                      DateTime expiresAt)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException(
                "User ID cannot be empty.",
                nameof(userId));

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException(
                "Token hash cannot be empty.",
                nameof(tokenHash));

        if (expiresAt <= createdAt)
            throw new ArgumentException(
                "Expiration must be after creation.");

        return new RefreshToken(
            id,
            userId,
            tokenHash,
            createdAt,
            expiresAt);
    }
}