using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.UnitRegistry;
using CLMS.Domain.Entities.Booking;
using CLMS.Infrastructure.Auth;
using CLMS.Shared.Dtos;
using CLMS.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using RegistryComponent = CLMS.Web.Components.Pages.Admin.UnitRegistry;

namespace CLMS.Tests;

public sealed class UnitRegistryComponentTests
{
    [Theory]
    [InlineData("populated")]
    [InlineData("empty")]
    [InlineData("error")]
    public async Task RendersRegistryRowsAndDistinctEmptyOrErrorStates(string scenario)
    {
        var repository = new ReadRepository(scenario);
        var services = Services(repository);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<RegistryComponent>()).ToHtmlString());
        Assert.Contains("Unit Registry", html);
        if (scenario == "populated")
        {
            Assert.Contains("ABCD2345", html);
            Assert.Contains("test-host", html);
            Assert.Contains("Not reported", html);
            Assert.Contains("Approve", html);
        }
        else if (scenario == "empty")
        {
            Assert.Contains("No computer units match", html);
            Assert.Contains("registration requests", html);
        }
        else
        {
            Assert.Contains("role=\"alert\"", html);
            Assert.Contains("Could not refresh", html);
            Assert.DoesNotContain("sensitive-database-detail", html);
        }
    }
    [Fact]
    public async Task LoadingStateIsVisibleUntilTheQueryCompletes()
    {
        var repository = new ReadRepository("empty") { Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var provider = Services(repository).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var component = await renderer.Dispatcher.InvokeAsync(() =>
        {
            var root = renderer.BeginRenderingComponent<RegistryComponent>();
            Assert.Contains("Loading registry", root.ToHtmlString());
            return root;
        });
        repository.Delay.SetResult();
        await component.QuiescenceTask;
        var html = await renderer.Dispatcher.InvokeAsync(component.ToHtmlString);
        Assert.DoesNotContain("Loading registry", html);
    }
    private static ServiceCollection Services(ReadRepository repository)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<DialogService>(); services.AddSingleton<ToastService>();
        services.AddSingleton(new UnitRegistryService(repository, new Labs(), new RefreshTokenHasher(),
            new AgentTokenService(Options.Create(new JwtOptions()), Options.Create(new AgentAuthOptions())), new Actor(), new DeviceProofVerifier()));
        return services;
    }
    private sealed class Actor : ICurrentUser { public Task<Guid> RequireAdminAsync() => Task.FromResult(Guid.NewGuid()); }
    private sealed class Labs : ILaboratoryRepository
    {
        public Task<IEnumerable<Laboratory>> GetAllAsync() => Task.FromResult<IEnumerable<Laboratory>>([new() { Id = 1, Name = "Test lab" }]);
        public Task<Laboratory?> GetByIdAsync(int id) => Task.FromResult<Laboratory?>(null);
    }
    private sealed class ReadRepository(string scenario) : IUnitRegistryRepository
    {
        public TaskCompletionSource? Delay;
        public async Task<RegistryPage<RegistryUnit>> UnitsAsync(int? lab, string? search, int page, int size, CancellationToken cancellationToken = default)
        {
            if (Delay is not null) await Delay.Task;
            if (scenario == "error") throw new InvalidOperationException("sensitive-database-detail");
            IReadOnlyList<RegistryUnit> items = scenario == "populated" ? [new(Guid.NewGuid(), 1, "Test lab", 1, "PC-001", "test-host", null, null, true, DateTime.UtcNow, null, false)] : [];
            return new(items, items.Count, 1, 25);
        }
        public Task<RegistryPage<RegistryRegistration>> RegistrationsAsync(string? status, int page, int size, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RegistryRegistration> items = scenario == "populated" ? [new(Guid.NewGuid(), 1, "Test lab", 2, "ABCD2345", "pending-host", "Windows", "1", "Pending", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), null)] : [];
            return Task.FromResult(new RegistryPage<RegistryRegistration>(items, items.Count, 1, 25));
        }
        public Task CleanupChallengesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsAuthorizedAsync(Guid id, int generation, Guid family, CancellationToken ct) => throw new NotSupportedException();
        public Task<T> TransactionAsync<T>(RegistryLock key, Func<IUnitRegistryTransaction, Task<T>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
