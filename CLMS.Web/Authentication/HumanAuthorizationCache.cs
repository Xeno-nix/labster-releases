using System.Collections.Frozen;
using System.Security.Claims;
using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace CLMS.Web.Authentication;

public sealed record CachedHumanAuthorization(Guid UserId, string SecurityStamp, Role Role,
    IReadOnlySet<string> Privileges);

/// <summary>Shares authorization reads across requests and circuits in this server process.</summary>
public sealed class HumanAuthorizationCache(IServiceScopeFactory scopes) : IDisposable
{
    private readonly object gate = new();
    private readonly MemoryCache entries = new(new MemoryCacheOptions());

    public async Task<CachedHumanAuthorization?> GetAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirstValue("principal_type") != "human" ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            principal.FindFirstValue("security_stamp") is not { Length: 64 } stamp)
            return null;

        var key = (userId, stamp);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lazy<Task<CachedHumanAuthorization?>> entry;
            lock (gate)
            {
                if (!entries.TryGetValue(key, out entry!))
                {
                    entry = new(() => LoadAsync(userId, stamp));
                    entries.Set(key, entry, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(30)
                    });
                }
            }

            CachedHumanAuthorization? result;
            try
            {
                // Cancelling one viewer must not cancel the shared database read.
                result = await entry.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                lock (gate)
                {
                    if (!IsCurrent(key, entry)) continue;
                    entries.Remove(key);
                }
                throw;
            }

            lock (gate)
            {
                // An admin change may have invalidated this entry while it was loading.
                if (!IsCurrent(key, entry)) continue;
                if (result is null) entries.Remove(key);
                return result;
            }
        }
    }

    public void InvalidateAll()
    {
        lock (gate) entries.Clear();
    }

    public void InvalidateUser(Guid userId)
    {
        lock (gate)
            foreach (var key in entries.Keys.OfType<(Guid UserId, string Stamp)>().Where(k => k.UserId == userId).ToArray())
                entries.Remove(key);
    }

    private bool IsCurrent((Guid, string) key, Lazy<Task<CachedHumanAuthorization?>> entry) =>
        entries.TryGetValue(key, out object? current) && ReferenceEquals(current, entry);

    private async Task<CachedHumanAuthorization?> LoadAsync(Guid userId, string stamp)
    {
        await using var scope = scopes.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindByIdAsync(userId);
        var crypto = scope.ServiceProvider.GetRequiredService<IAccountSecurityCrypto>();
        if (user is null || user.DeletedAt.HasValue || user.IsLockedOut || !user.PasswordChanged ||
            string.IsNullOrWhiteSpace(user.PasswordHash) || !Privileges.IsStaff(user.UserRole) ||
            !crypto.StampMatches(user, stamp)) return null;

        var snapshot = await scope.ServiceProvider.GetRequiredService<IAuthorizationRepository>().GetAsync(userId);
        if (snapshot is null || !snapshot.IsActive || snapshot.Role != user.UserRole) return null;
        return new(userId, stamp, snapshot.Role, snapshot.Effective.ToFrozenSet(StringComparer.Ordinal));
    }

    public void Dispose() => entries.Dispose();
}
