using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace CLMS.Web.Authentication;

public sealed class HumanRevalidatingStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory scopes;
    private readonly AuthorizationChanges changes;
    private readonly HumanAuthorizationCache cache;
    private readonly ILogger logger;
    private readonly object refreshGate = new();
    private int refreshVersion;
    private bool disposed;

    public HumanRevalidatingStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes,
        AuthorizationChanges changes, HumanAuthorizationCache cache) : base(loggerFactory)
    {
        this.scopes = scopes; this.changes = changes;
        this.cache = cache;
        logger = loggerFactory.CreateLogger<HumanRevalidatingStateProvider>();
        changes.Changed += Changed;
    }

    private void Changed()
    {
        int version;
        lock (refreshGate)
        {
            if (disposed) return;
            version = ++refreshVersion;
        }
        _ = RecheckAsync(version);
    }

    private async Task RecheckAsync(int version)
    {
        Task<AuthenticationState> original;
        // The host may not have assigned the circuit's initial state yet.
        try { original = GetAuthenticationStateAsync(); }
        catch (InvalidOperationException) { return; }

        AuthenticationState state;
        try
        {
            state = await original;
            if (state.User.Identity?.IsAuthenticated != true) return;
            if (await cache.GetAsync(state.User) is null) state = Anonymous();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to refresh the circuit's authorization state.");
            state = Anonymous();
        }

        lock (refreshGate)
        {
            // Never restore a state superseded by a newer change or a sign-out.
            if (!disposed && version == refreshVersion && ReferenceEquals(GetAuthenticationStateAsync(), original))
                SetAuthenticationState(Task.FromResult(state));
        }
    }

    private static AuthenticationState Anonymous() => new(new ClaimsPrincipal(new ClaimsIdentity()));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (refreshGate)
            {
                disposed = true;
                ++refreshVersion;
                changes.Changed -= Changed;
            }
        }
        base.Dispose(disposing);
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        var valid = false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            valid = await scope.ServiceProvider.GetRequiredService<HumanSessionValidator>().IsCurrentAsync(state.User);
            return valid;
        }
        finally
        {
            if (!valid && !cancellationToken.IsCancellationRequested &&
                Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                cache.InvalidateUser(userId);
        }
    }
}
