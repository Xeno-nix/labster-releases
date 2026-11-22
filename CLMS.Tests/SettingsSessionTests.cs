using System.Net;
using System.Net.Http.Json;
using CLMS.Agent.Services;
using CLMS.Shared.Dtos;
using Xunit;

namespace CLMS.Tests;

public sealed class SettingsSessionTests
{
    [Fact]
    public async Task LoginRequiresServerAuthorizationAndLockDropsAccess()
    {
        var handler = new Server();
        var session = new SettingsSession(new HttpClient(handler));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(session.RequireAccessAsync);
        await session.SignInAsync("https://lab.test", "admin@lab.test", "password");
        Assert.True(session.IsUnlocked);
        Assert.Equal(1, handler.Revocations);
        Assert.Equal("human-access", handler.LastBearer);
        session.Lock();
        Assert.False(session.IsUnlocked);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(session.RequireAccessAsync);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task DeniedExpiredOrUnavailableAuthorizationLocksSettings(HttpStatusCode status)
    {
        var handler = new Server();
        var session = new SettingsSession(new HttpClient(handler));
        await session.SignInAsync("https://lab.test", "admin@lab.test", "password");
        handler.AccessStatus = status;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(session.RequireAccessAsync);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task FacultyCannotUnlockEvenWithValidLoginTokens()
    {
        var session = new SettingsSession(new HttpClient(new Server { AccessStatus = HttpStatusCode.Forbidden }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.SignInAsync("https://lab.test", "faculty@lab.test", "password"));
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task EmailCodeMustCompleteBeforeOpeningSettings()
    {
        var handler = new Server { TwoFactor = true };
        var session = new SettingsSession(new HttpClient(handler));
        await session.SignInAsync("https://lab.test", "admin@lab.test", "password");
        Assert.True(session.NeedsCode);
        Assert.False(session.IsUnlocked);
        await session.ResendAsync();
        await session.VerifyAsync("123456");
        Assert.True(session.IsUnlocked);
        Assert.False(session.NeedsCode);
    }

    [Fact]
    public async Task LockDuringLoginPreventsLateResponseFromUnlocking()
    {
        var pending = new TaskCompletionSource();
        var handler = new Server { PendingLogin = pending.Task };
        var session = new SettingsSession(new HttpClient(handler));
        var login = session.SignInAsync("https://lab.test", "admin@lab.test", "password");
        session.Lock();
        pending.SetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => login);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task SettingsStayLockedUntilRoleCheckFinishes()
    {
        var pending = new TaskCompletionSource();
        var handler = new Server { PendingAuthorization = pending.Task };
        var session = new SettingsSession(new HttpClient(handler));
        var login = session.SignInAsync("https://lab.test", "faculty@lab.test", "password");
        Assert.False(session.IsUnlocked);
        handler.AccessStatus = HttpStatusCode.Forbidden;
        pending.SetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => login);
        Assert.False(session.IsUnlocked);
    }

    private sealed class Server : HttpMessageHandler
    {
        public HttpStatusCode AccessStatus = HttpStatusCode.OK;
        public bool TwoFactor;
        public int Revocations;
        public string? LastBearer;
        public Task? PendingLogin;
        public Task? PendingAuthorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/auth/login" && PendingLogin is not null) await PendingLogin;
            if (path == "/api/agent/settings/access")
            {
                if (PendingAuthorization is not null) await PendingAuthorization;
                LastBearer = request.Headers.Authorization?.Parameter;
                return new(AccessStatus);
            }
            if (path == "/api/auth/logout") { Revocations++; return new(HttpStatusCode.OK); }
            if ((path == "/api/auth/login" && TwoFactor) || path.EndsWith("resend"))
                return new(path.EndsWith("resend") ? HttpStatusCode.OK : HttpStatusCode.Accepted)
                { Content = JsonContent.Create(new EmailLoginReceipt("challenge", DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow)) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new AuthTokensDto("human-access", "refresh")) };
        }
    }
}
