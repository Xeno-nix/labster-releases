namespace CLMS.Domain.Entities.Authentication;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public  Guid UserId { get; set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    private DateTime ExpiresAt { get; }
    private DateTime? RevokedAt { get; set; }
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired =>
        DateTime.UtcNow >= ExpiresAt;
    public bool IsActive =>
        !IsRevoked && !IsExpired;

    private RefreshToken(){}

    public RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime createdAt,
        DateTime expiresAt)
    {
        Id = id;
        UserId = userId;
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

    /// <summary>
    /// Creates a refresh token entity to be stored in
    /// the database.
    /// 
    /// </summary>
    /// <param name="userId">Token owner</param>
    /// <param name="tokenHash">Hashed base64 encoded token</param>
    /// <param name="createdAt"></param>
    /// <param name="expiresAt"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static RefreshToken Create(Guid userId,
                                      string tokenHash,
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

        var createdAt = DateTime.UtcNow;
        
        if (expiresAt <= createdAt)
            throw new ArgumentException(
                "Expiration must be after creation.");

        return new RefreshToken(
            Guid.NewGuid(),
            userId,
            tokenHash,
            createdAt,
            expiresAt);
    }
}