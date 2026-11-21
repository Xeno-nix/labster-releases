using System.Security.Claims;
using CLMS.Application.Authorization;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Enums;
using CLMS.Infrastructure.Auth;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CLMS.Tests;

public sealed class HumanAuthorizationCacheTests
{
    [Fact]
    public async Task ConcurrentViewersShareOneLoadAndWarmPoliciesDoNotReadTheDatabase()
    {
        await using var fixture = new Fixture();
        var pending = new TaskCompletionSource<AuthorizationSnapshot?>();
        fixture.Store.ReadSnapshot = (_, _) => pending.Task;
        var viewers = Enumerable.Range(0, 12).Select(_ => fixture.Cache.GetAsync(fixture.Principal)).ToArray();
        Assert.Equal(1, fixture.Store.SnapshotReads);
        Assert.All(viewers, task => Assert.False(task.IsCompleted));
        pending.SetResult(fixture.Snapshot());
        var results = await Task.WhenAll(viewers);
        Assert.All(results, result => Assert.Same(results[0], result));

        var userReads = fixture.Users.FindByIdCalls;
        using var request = fixture.Services.CreateScope();
        var warm = request.ServiceProvider.GetRequiredService<HumanAuthorizationCache>().GetAsync(fixture.Principal);
        Assert.True(warm.IsCompletedSuccessfully);
        Assert.Same(results[0], await warm);
        var handler = new PrivilegeAuthorizationHandler(fixture.Cache, fixture.Store);
        foreach (var privilege in new[] { Privileges.UnitsView, Privileges.UnitsMonitor, Privileges.LogbookView })
        {
            var context = new AuthorizationHandlerContext([new PrivilegeRequirement(privilege)], fixture.Principal, null);
            var check = handler.HandleAsync(context);
            Assert.True(check.IsCompletedSuccessfully);
            await check;
            Assert.Equal(results[0]!.Privileges.Contains(privilege), context.HasSucceeded);
        }
        Assert.Equal(1, fixture.Store.SnapshotReads);
        Assert.Equal(userReads, fixture.Users.FindByIdCalls);
    }

    [Fact]
    public async Task ChangesInvalidateBeforeNotifyingAndRefreshGrantsRevocationsAndResets()
    {
        await using var fixture = new Fixture();
        Assert.Contains(Privileges.UnitsMonitor, (await fixture.Cache.GetAsync(fixture.Principal))!.Privileges);
        Task<CachedHumanAuthorization?>? observed = null;
        fixture.Changes.Changed += () => observed = fixture.Cache.GetAsync(fixture.Principal);
        fixture.Store.Roles[Role.Faculty].Remove(Privileges.UnitsMonitor);
        fixture.Changes.NotifyChanged();
        Assert.DoesNotContain(Privileges.UnitsMonitor, (await observed!)!.Privileges);
        fixture.Store.Overrides[fixture.User.Id] = new() { [Privileges.UnitsMonitor] = true };
        fixture.Changes.NotifyChanged();
        Assert.Contains(Privileges.UnitsMonitor, (await observed!)!.Privileges);
        fixture.Store.Overrides[fixture.User.Id][Privileges.UnitsMonitor] = false;
        fixture.Changes.NotifyChanged();
        Assert.DoesNotContain(Privileges.UnitsMonitor, (await observed!)!.Privileges);
        fixture.Store.Roles[Role.Faculty].Add(Privileges.UnitsMonitor);
        fixture.Store.Overrides[fixture.User.Id].Remove(Privileges.UnitsMonitor);
        fixture.Changes.NotifyChanged();
        Assert.Contains(Privileges.UnitsMonitor, (await observed!)!.Privileges);
        Assert.Equal(5, fixture.Store.SnapshotReads);
    }

    [Fact]
    public async Task InvalidationDuringLoadDiscardsTheOldResultForEveryWaitingViewer()
    {
        await using var fixture = new Fixture();
        var oldRead = new TaskCompletionSource<AuthorizationSnapshot?>();
        var oldSnapshot = fixture.Snapshot();
        fixture.Store.ReadSnapshot = (_, _) => oldRead.Task;
        var before = fixture.Cache.GetAsync(fixture.Principal);
        var secondViewer = fixture.Cache.GetAsync(fixture.Principal);
        fixture.Store.Roles[Role.Faculty].Remove(Privileges.UnitsMonitor);
        fixture.Changes.NotifyChanged();
        fixture.Store.ReadSnapshot = (_, _) => Task.FromResult<AuthorizationSnapshot?>(fixture.Snapshot());
        var after = await fixture.Cache.GetAsync(fixture.Principal);
        oldRead.SetResult(oldSnapshot);
        Assert.Same(after, await before);
        Assert.Same(after, await secondViewer);
        Assert.DoesNotContain(Privileges.UnitsMonitor, after!.Privileges);
        Assert.Equal(2, fixture.Store.SnapshotReads);
    }

    [Fact]
    public async Task FailedLoadsCanRetryAndCancellingOneViewerDoesNotCancelTheSharedRead()
    {
        await using var fixture = new Fixture();
        fixture.Store.ReadSnapshot = (_, _) => Task.FromException<AuthorizationSnapshot?>(new InvalidOperationException("offline"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Cache.GetAsync(fixture.Principal));
        var pending = new TaskCompletionSource<AuthorizationSnapshot?>();
        fixture.Store.ReadSnapshot = (_, _) => pending.Task;
        using var cancellation = new CancellationTokenSource();
        var cancelled = fixture.Cache.GetAsync(fixture.Principal, cancellation.Token);
        var otherViewer = fixture.Cache.GetAsync(fixture.Principal);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        pending.SetResult(fixture.Snapshot());
        Assert.NotNull(await otherViewer);
        Assert.Same(await otherViewer, await fixture.Cache.GetAsync(fixture.Principal));
        Assert.Equal(2, fixture.Store.SnapshotReads);
    }

    [Fact]
    public async Task AnonymousInvalidAndChangedAccountsCannotReuseAnotherIdentitysSnapshot()
    {
        await using var fixture = new Fixture();
        Assert.Null(await fixture.Cache.GetAsync(new ClaimsPrincipal()));
        Assert.Equal(0, fixture.Users.FindByIdCalls);
        var original = fixture.Principal;
        Assert.NotNull(await fixture.Cache.GetAsync(original));
        var forgedStamp = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, fixture.User.Id.ToString()), new Claim("principal_type", "human"),
            new Claim("security_stamp", new string('0', 64))], "test"));
        Assert.Null(await fixture.Cache.GetAsync(forgedStamp));
        fixture.User.UserRole = Role.Admin;
        fixture.Changes.NotifyChanged();
        Assert.Null(await fixture.Cache.GetAsync(original));
        Assert.Equal(Role.Admin, (await fixture.Cache.GetAsync(fixture.Principal))!.Role);
        fixture.User.LockoutEnd = DateTime.UtcNow.AddMinutes(1);
        fixture.Cache.InvalidateUser(fixture.User.Id);
        Assert.Null(await fixture.Cache.GetAsync(fixture.Principal));
    }

    [Fact]
    public async Task MultipleCircuitsRefreshTogetherAndOverlappingChangesPublishOnlyTheLatestAccess()
    {
        await using var fixture = new Fixture();
        using var first = fixture.CreateState();
        using var second = fixture.CreateState();
        var firstNotifications = 0;
        var secondNotifications = 0;
        var refreshed = new TaskCompletionSource();
        first.AuthenticationStateChanged += _ => { Interlocked.Increment(ref firstNotifications); refreshed.TrySetResult(); };
        second.AuthenticationStateChanged += _ => Interlocked.Increment(ref secondNotifications);
        var oldRead = new TaskCompletionSource<AuthorizationSnapshot?>();
        var oldSnapshot = fixture.Snapshot();
        fixture.Store.ReadSnapshot = (_, _) => oldRead.Task;
        fixture.Changes.NotifyChanged();
        Assert.Equal(1, fixture.Store.SnapshotReads);
        fixture.Store.Roles[Role.Faculty].Remove(Privileges.UnitsMonitor);
        fixture.Store.ReadSnapshot = (_, _) => Task.FromResult<AuthorizationSnapshot?>(fixture.Snapshot());
        fixture.Changes.NotifyChanged();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        oldRead.SetResult(oldSnapshot);
        // Joining the obsolete read drains both older refreshes without permitting a stale notification.
        await fixture.Cache.GetAsync(fixture.Principal);
        Assert.Equal(1, firstNotifications);
        Assert.Equal(1, secondNotifications);
        Assert.DoesNotContain(Privileges.UnitsMonitor, (await fixture.Cache.GetAsync((await first.GetAuthenticationStateAsync()).User))!.Privileges);
        Assert.Equal(2, fixture.Store.SnapshotReads);
    }

    [Fact]
    public async Task SuccessfulPeriodicChecksPreserveCachedPrivilegesWithoutNotifyingTheCircuit()
    {
        await using var fixture = new Fixture();
        using var state = fixture.CreateState();
        var authentication = await state.GetAuthenticationStateAsync();
        var snapshot = await fixture.Cache.GetAsync(authentication.User);
        var reads = fixture.Users.FindByIdCalls;
        var notifications = 0;
        state.AuthenticationStateChanged += _ => notifications++;
        Assert.True(await Revalidate(state, authentication));
        Assert.Equal(reads + 1, fixture.Users.FindByIdCalls);
        Assert.Same(snapshot, await fixture.Cache.GetAsync(authentication.User));
        Assert.Equal(1, fixture.Store.SnapshotReads);
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("deleted")]
    [InlineData("password")]
    public async Task PeriodicChecksInvalidateTheCacheWhenTheAccountBecomesInvalid(string change)
    {
        await using var fixture = new Fixture();
        using var state = fixture.CreateState();
        var authentication = await state.GetAuthenticationStateAsync();
        Assert.NotNull(await fixture.Cache.GetAsync(authentication.User));
        switch (change)
        {
            case "locked": fixture.User.LockoutEnd = DateTime.UtcNow.AddMinutes(5); break;
            case "deleted": fixture.User.DeletedAt = DateTime.UtcNow; break;
            case "password": fixture.User.PasswordHash = "replacement-password-hash"; break;
        }
        Assert.False(await Revalidate(state, authentication));
        Assert.Null(await fixture.Cache.GetAsync(authentication.User));
    }

    private static Task<bool> Revalidate(HumanRevalidatingStateProvider state, AuthenticationState authentication) =>
        (Task<bool>)typeof(HumanRevalidatingStateProvider).GetMethod("ValidateAuthenticationStateAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(state, [authentication, CancellationToken.None])!;

    [Fact]
    public async Task DisposedOrSignedOutCircuitsAreNotRestoredByAnOutstandingRefresh()
    {
        await using var fixture = new Fixture();
        using var state = fixture.CreateState();
        var notifications = 0;
        state.AuthenticationStateChanged += _ => notifications++;
        var pending = new TaskCompletionSource<AuthorizationSnapshot?>();
        fixture.Store.ReadSnapshot = (_, _) => pending.Task;
        fixture.Changes.NotifyChanged();
        state.SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        pending.SetResult(fixture.Snapshot());
        Assert.False((await state.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal(1, notifications);
        ((IDisposable)state).Dispose();
        fixture.Changes.NotifyChanged();
        Assert.Equal(1, fixture.Store.SnapshotReads);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public CLMS.Domain.Entities.Identity.User User { get; } = PrivilegeAuthorizationTests.Account();
        public EmailLoginTests.TestUsers Users { get; }
        public TestAuthorizationStore Store { get; }
        public ServiceProvider Services { get; }
        public HumanAuthorizationCache Cache => Services.GetRequiredService<HumanAuthorizationCache>();
        public AuthorizationChanges Changes => Services.GetRequiredService<AuthorizationChanges>();
        public AccountSecurityCrypto Crypto { get; } = new(Options.Create(new JwtOptions { Key = new string('k', 64) }));
        public ClaimsPrincipal Principal => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()), new Claim(ClaimTypes.Role, User.UserRole.ToString()),
            new Claim("principal_type", "human"), new Claim("security_stamp", Crypto.Stamp(User))], "Cookies"));

        public Fixture(Action<IServiceCollection>? configure = null)
        {
            Users = new(User); Store = new(Users);
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton<IUserRepository>(Users); services.AddSingleton<IAccountSecurityCrypto>(Crypto);
            services.AddSingleton<IAuthorizationRepository>(Store); services.AddScoped<HumanSessionValidator>();
            services.AddSingleton<HumanAuthorizationCache>(); services.AddSingleton<AuthorizationChanges>();
            configure?.Invoke(services);
            Services = services.BuildServiceProvider();
        }

        public AuthorizationSnapshot Snapshot() => new(User.Id, User.UserRole, true,
            Store.Roles[User.UserRole].ToHashSet(), new Dictionary<string, bool>());

        public HumanRevalidatingStateProvider CreateState()
        {
            var state = new HumanRevalidatingStateProvider(Services.GetRequiredService<ILoggerFactory>(),
                Services.GetRequiredService<IServiceScopeFactory>(), Changes, Cache);
            state.SetAuthenticationState(Task.FromResult(new AuthenticationState(Principal)));
            return state;
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
