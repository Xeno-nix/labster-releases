using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CLMS.Application.UnitRegistry;
using CLMS.Infrastructure.Auth;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using AuthenticationSchemes = CLMS.Web.Authentication.AuthenticationSchemes;

namespace CLMS.Tests;

public sealed class AgentAuthorizationTests
{
    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha256)]
    [InlineData(SecurityAlgorithms.HmacSha256Signature)]
    public async Task AgentHumanAndAdministratorAuthorizationStaySeparatedAndRevocationIsImmediate(string signingAlgorithm)
    {
        var key = new string('A', 64);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "human", ["Jwt:Key"] = key, ["Jwt:SigningAlgorithm"] = signingAlgorithm });
        var repository = new AuthorizationRepository();
        builder.Services.AddSingleton<IUnitRegistryRepository>(repository);
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TestPrivilegeHandler>();
        builder.Services.AddAuthentication(AuthenticationSchemes.Cookie)
            .AddCookie(AuthenticationSchemes.Cookie, o =>
            {
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
            })
            .AddJwtBearer(AuthenticationSchemes.AgentBearer, o => AgentAuthentication.Configure(o, builder.Configuration))
            .AddJwtBearer(AuthenticationSchemes.Bearer, o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "test", ValidateAudience = true, ValidAudience = "human",
                    ValidateLifetime = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateIssuerSigningKey = true
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = AgentAuthentication.RequireHuman };
            });
        builder.Services.AddAuthorization(o =>
        {
            AgentAuthentication.AddPolicies(o);
            o.AddPolicy("JwtPolicy", p => p.AddAuthenticationSchemes(AuthenticationSchemes.Bearer).RequireAuthenticatedUser());
        });
        await using var app = builder.Build();
        app.UseMiddleware<UnitRegistryMiddleware>();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/api/agent/protected", () => "agent").RequireAuthorization("AgentOnly");
        app.MapGet("/human", () => "human").RequireAuthorization("JwtPolicy");
        app.MapGet("/api/admin/protected", () => "admin").RequireAuthorization("UnitRegistryAdmin");
        app.MapGet("/signin-test-admin", async context => await context.SignInAsync(AuthenticationSchemes.Cookie,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Admin")], AuthenticationSchemes.Cookie))));
        await app.StartAsync();
        using var client = app.GetTestClient(); client.BaseAddress = new("https://localhost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/agent/protected")).StatusCode);
        var tokens = new AgentTokenService(Options.Create(new JwtOptions { Issuer = "test", Key = key, SigningAlgorithm = signingAlgorithm }), Options.Create(new AgentAuthOptions()));
        var issued = tokens.Issue(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.UtcNow);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issued.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/agent/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/human")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/protected")).StatusCode);
        repository.Active = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/agent/protected")).StatusCode);
        repository.Active = true; repository.Generation = 2;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/agent/protected")).StatusCode);
        var human = new JwtSecurityToken("test", "human", [new("sub", Guid.NewGuid().ToString()), new("role", "Admin")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), "HS256"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(human));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/human")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/agent/protected")).StatusCode);
        var signin = await client.GetAsync("/signin-test-admin");
        client.DefaultRequestHeaders.Add("Cookie", signin.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/protected")).StatusCode);
        repository.Unavailable = true;
        client.DefaultRequestHeaders.Authorization = new("Bearer", issued.AccessToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/agent/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("http://localhost/api/agent/protected")).StatusCode);
    }

    private sealed class AuthorizationRepository : IUnitRegistryRepository
    {
        public bool Active = true, Unavailable;
        public Task CleanupChallengesAsync(CancellationToken ct) => Task.CompletedTask;
        public int Generation = 1;
        public Task<bool> IsAuthorizedAsync(Guid id, int generation, Guid family, CancellationToken ct) =>
            Unavailable ? throw new UnavailableDatabase() : Task.FromResult(Active && Generation == generation);
        public Task<T> TransactionAsync<T>(RegistryLock key, Func<IUnitRegistryTransaction, Task<T>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegistryPage<RegistryUnit>> UnitsAsync(int? lab, string? search, int page, int size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class UnavailableDatabase : System.Data.Common.DbException;
}
