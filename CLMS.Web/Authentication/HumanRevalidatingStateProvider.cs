using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace CLMS.Web.Authentication;

public sealed class HumanRevalidatingStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory scopes;
    private readonly AuthorizationChanges changes;
    public HumanRevalidatingStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes, AuthorizationChanges changes) : base(loggerFactory)
    {
        this.scopes = scopes; this.changes = changes;
        changes.Changed += Changed;
    }
    private void Changed() => _ = RecheckAsync();
    private async Task RecheckAsync()
    {
        try
        {
            var state = await GetAuthenticationStateAsync();
            using var scope = scopes.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<HumanSessionValidator>().IsCurrentAsync(state.User))
                state = new(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));
            SetAuthenticationState(Task.FromResult(state));
        }
        catch { SetAuthenticationState(Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal()))); }
    }
    protected override void Dispose(bool disposing) { if (disposing) changes.Changed -= Changed; base.Dispose(disposing); }
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HumanSessionValidator>().IsCurrentAsync(state.User);
    }
}
