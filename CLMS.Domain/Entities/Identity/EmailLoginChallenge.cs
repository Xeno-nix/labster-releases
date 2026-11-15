namespace CLMS.Domain.Entities.Identity;

/// <summary>The short-lived proof permits five guesses and cannot be reused after successful verification.</summary>
public sealed class EmailLoginChallenge(DateTime createdAt, string codeHash)
{
    public DateTime ExpiresAt { get; } = createdAt.AddMinutes(5);
    public DateTime LastSentAt { get; private set; } = createdAt;
    public string CodeHash { get; private set; } = codeHash;
    public int FailedAttempts { get; private set; }
    public bool Consumed { get; private set; }
    public bool IsActive(DateTime now) => !Consumed && FailedAttempts < 5 && now < ExpiresAt;

    public bool TryConsume(bool correct, DateTime now)
    {
        if (!IsActive(now)) return false;
        if (!correct) { FailedAttempts++; return false; }
        Consumed = true;
        return true;
    }

    public void Resend(string replacementHash, DateTime now)
    {
        if (!IsActive(now)) throw new InvalidOperationException("This sign-in request has expired. Sign in again.");
        if (now < LastSentAt.AddMinutes(1)) throw new InvalidOperationException("Wait 60 seconds before resending.");
        CodeHash = replacementHash;
        LastSentAt = now;
    }
}
