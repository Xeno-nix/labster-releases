using System.Threading.Channels;
using CLMS.Application.Identity.Interfaces;

namespace CLMS.Infrastructure.Auth;

public sealed class PasswordRecoveryQueue(TimeProvider clock) : IPasswordRecoveryQueue
{
    private readonly Channel<string> queue = Channel.CreateBounded<string>(new BoundedChannelOptions(100) { SingleReader = true });
    private readonly Dictionary<string, Queue<DateTimeOffset>> requests = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public PasswordRecoveryRequestResult Enqueue(string normalizedEmail)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var pair in requests.ToArray())
            {
                while (pair.Value.TryPeek(out var date) && date <= now.AddMinutes(-15)) pair.Value.Dequeue();
                if (pair.Value.Count == 0) requests.Remove(pair.Key);
            }
            if (!requests.TryGetValue(normalizedEmail, out var history))
            {
                if (requests.Count >= 5000) return new(true, now.AddMinutes(1), now);
                requests[normalizedEmail] = history = new();
            }
            var retryAt = history.Count > 0 ? history.Last().AddMinutes(1) : now;
            if (history.Count >= 5 && history.Peek().AddMinutes(15) > retryAt) retryAt = history.Peek().AddMinutes(15);
            if (now < retryAt) return new(true, retryAt, now);
            // Count accepted attempts even when the bounded queue is full. Do not expose queue/delivery state.
            history.Enqueue(now);
            queue.Writer.TryWrite(normalizedEmail);
            retryAt = history.Last().AddMinutes(1);
            if (history.Count >= 5 && history.Peek().AddMinutes(15) > retryAt) retryAt = history.Peek().AddMinutes(15);
            return new(false, retryAt, now);
        }
    }
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) => queue.Reader.ReadAllAsync(cancellationToken);
}
