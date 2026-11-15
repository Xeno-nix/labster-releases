namespace CLMS.Application.Identity.Interfaces;

/// <summary>Public retry timing is calculated before account lookup and reveals no delivery or registration state.</summary>
public sealed record PasswordRecoveryRequestResult(bool RateLimited, DateTimeOffset RetryAt, DateTimeOffset ServerTime);

/// <summary>Queues addresses before account lookup to keep public responses independent of account existence.</summary>
public interface IPasswordRecoveryQueue
{
    PasswordRecoveryRequestResult Enqueue(string normalizedEmail);
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken);
}
