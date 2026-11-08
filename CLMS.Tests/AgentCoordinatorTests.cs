using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CLMS.Agent.Core;
using CLMS.Infrastructure.Auth;
using CLMS.Shared.Dtos;
using CLMS.Shared.Security;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentCoordinatorTests
{
    [Fact]
    public async Task ApprovalAndRestartAfterMonthsAuthenticateWithoutAnotherRegistration()
    {
        using var f = new Fixture();
        using (var agent = f.Create())
        {
            await agent.ConfigureAsync("https://lab.example", default);
            await agent.RegisterAsync(1, 1, "test", default);
            await agent.TickAsync(default);
            Assert.Equal("Pending", agent.Status.Phase);
            f.Server.Status = "Approved";
            await agent.TickAsync(default);
            Assert.Equal("Connected", agent.Status.Phase);
            Assert.Null(f.Vault.State!.Registration);
            Assert.DoesNotContain("accessToken", JsonSerializer.Serialize(f.Vault.State), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refresh", JsonSerializer.Serialize(f.Vault.State), StringComparison.OrdinalIgnoreCase);
        }
        f.Clock.Now = f.Clock.Now.AddMonths(8);
        using var restarted = f.Create();
        await restarted.TickAsync(default);
        Assert.Equal("Connected", restarted.Status.Phase);
        Assert.Equal(1, f.Server.Registrations);
        Assert.Equal(1, f.Server.Claims);
        Assert.Equal(2, f.Server.Authentications);
    }

    [Fact]
    public async Task LostCompletionResponseRecoversWithoutActivatingTwice()
    {
        using var f = new Fixture();
        f.Server.LoseClaim = true;
        using (var agent = await f.Enroll(complete: false))
        {
            await agent.TickAsync(default);
            Assert.Equal("Offline", agent.Status.Phase);
            Assert.NotNull(f.Vault.State!.Registration);
            Assert.NotNull(f.Vault.State.Identity);
        }
        using var restarted = f.Create();
        await restarted.TickAsync(default);
        Assert.Equal("Connected", restarted.Status.Phase);
        Assert.Equal(1, f.Server.Claims);
    }

    [Fact]
    public async Task ConcurrentCallsShareRenewalAndUnauthorizedOperationRetriesOnce()
    {
        using var f = new Fixture();
        using var agent = await f.Enroll();
        f.Clock.Now = f.Clock.Now.AddMinutes(14);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => agent.TickAsync(default)));
        Assert.Equal(2, f.Server.Authentications);
        f.Server.UnauthorizedHeartbeats = 1;
        await agent.TickAsync(default);
        Assert.Equal(3, f.Server.Authentications);
        Assert.Equal("Connected", agent.Status.Phase);
        f.Server.UnauthorizedHeartbeats = 2;
        await agent.TickAsync(default);
        Assert.Equal(4, f.Server.Authentications); // One retry only, then recovery.
        Assert.Equal("RecoveryRequired", agent.Status.Phase);
    }

    [Fact]
    public async Task OutageRetainsIdentityAndRevocationRequiresRecovery()
    {
        using var f = new Fixture();
        using var agent = await f.Enroll();
        var identity = f.Vault.State!.Identity;
        var key = f.Vault.State.KeyId;
        f.Server.Offline = true;
        f.Clock.Now = f.Clock.Now.AddMonths(4);
        for (var i = 0; i < 30; i++) await agent.TickAsync(default);
        Assert.Equal("Offline", agent.Status.Phase);
        Assert.Equal(identity, f.Vault.State.Identity);
        Assert.Equal(key, f.Vault.State.KeyId);
        Assert.Null(f.Vault.State.Rotation);
        f.Server.Offline = false;
        await agent.TickAsync(default);
        Assert.Equal("Connected", agent.Status.Phase);
        f.Server.Revoked = true;
        await agent.TickAsync(default);
        Assert.Equal("RecoveryRequired", agent.Status.Phase);
        Assert.Equal(identity, f.Vault.State.Identity); // Never erase recovery evidence automatically.
        Assert.False(agent.Status.CanRegister);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RotationRecoversAcrossRestartWithAndWithoutServerCommit(bool committed)
    {
        using var f = new Fixture();
        string oldKey;
        using (var agent = await f.Enroll())
        {
            oldKey = f.Vault.State!.KeyId!;
            f.Server.LoseRotation = committed;
            f.Server.FailBeforeRotation = !committed;
            await Assert.ThrowsAsync<HttpRequestException>(() => agent.RotateKeyAsync(default));
            Assert.NotNull(f.Vault.State.Rotation?.Replacement);
            Assert.True(f.Keys.Contains(oldKey));
        }
        using var restarted = f.Create();
        await restarted.TickAsync(default);
        Assert.Equal("Connected", restarted.Status.Phase);
        Assert.Null(f.Vault.State!.Rotation);
        Assert.NotEqual(oldKey, f.Vault.State.KeyId);
        Assert.False(f.Keys.Contains(oldKey));
        Assert.Equal(f.Server.Identity, f.Vault.State.Identity);
        Assert.Equal(1, f.Server.Rotations);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Expired")]
    public async Task RetryUsesNewKeyMaterial(string terminal)
    {
        using var f = new Fixture();
        using var agent = await f.Enroll(complete: false);
        var oldKey = f.Vault.State!.KeyId!;
        f.Server.Status = terminal;
        await agent.TickAsync(default);
        Assert.True(agent.Status.CanRetry);
        await agent.RetryAsync(default);
        await agent.RegisterAsync(1, 1, "test", default);
        Assert.NotEqual(oldKey, f.Vault.State.KeyId);
        Assert.False(f.Keys.Contains(oldKey));
        Assert.Equal(2, f.Server.Registrations);
    }

    [Fact]
    public async Task LostPrivateKeyDoesNotSilentlyReplaceIdentity()
    {
        using var f = new Fixture();
        using (var agent = await f.Enroll()) { }
        var keyId = f.Vault.State!.KeyId!;
        await f.Keys.DeleteAsync(keyId, default);
        using var restarted = f.Create();
        await restarted.TickAsync(default);
        Assert.Equal("StorageError", restarted.Status.Phase);
        Assert.Equal(keyId, f.Vault.State.KeyId);
        Assert.Equal(1, f.Server.Registrations);
    }

    [Fact]
    public async Task FirstRunRetriesConnectivityAndLocalMessagesExposeNoCredential()
    {
        using var f = new Fixture();
        using var agent = f.Create();
        await agent.ConfigureAsync("https://lab.example", default);
        f.Server.Offline = true;
        await agent.TickAsync(default);
        Assert.Equal("Offline", agent.Status.Phase);
        f.Server.Offline = false;
        await agent.TickAsync(default);
        Assert.True(agent.Status.CanRegister);
        using var stream = new MemoryStream();
        await AgentIpc.WriteAsync(stream, new IpcResponse(agent.Status), default);
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("privateKey", json);
        Assert.DoesNotContain("accessToken", json);
        stream.Position = 0;
        Assert.Equal("RegistrationRequired", (await AgentIpc.ReadAsync<IpcResponse>(stream, default)).Status!.Phase);
        using var oversized = new MemoryStream([0x7f, 0xff, 0xff, 0xff]);
        await Assert.ThrowsAsync<IOException>(() => AgentIpc.ReadAsync<IpcResponse>(oversized, default));
    }

    [Theory]
    [InlineData("http://lab.example")]
    [InlineData("https://user:password@lab.example")]
    [InlineData("https://lab.example/api")]
    [InlineData("https://lab.example/?token=unsafe")]
    public void SetupRejectsInsecureOrigins(string address) => Assert.Throws<ArgumentException>(() => AgentApi.ValidateAddress(address));

    [Fact]
    public void ReconnectDelayIsBounded()
    { for (var i = 0; i < 100; i++) Assert.InRange(AgentCoordinator.ReconnectDelay(i).TotalSeconds, 1.6, 60); }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Vault : IAgentVault
    {
        public AgentState? State;
        public Task<AgentState?> ReadAsync(CancellationToken ct) => Task.FromResult(State);
        public Task WriteAsync(AgentState state, CancellationToken ct) { State = state; return Task.CompletedTask; }
    }
    private sealed class TestKeys : IDeviceKeyStore, IDisposable
    {
        private readonly Dictionary<string, ECDsa> values = [];
        public bool Contains(string id) => values.ContainsKey(id);
        public Task<string> CreateAsync(string id, CancellationToken ct)
        { if (!values.ContainsKey(id)) values[id] = ECDsa.Create(ECCurve.NamedCurves.nistP256); return PublicKeyAsync(id, ct); }
        public Task<string> PublicKeyAsync(string id, CancellationToken ct) => Task.FromResult(DeviceProtocol.Encode(Get(id).ExportSubjectPublicKeyInfo()));
        public Task<string> SignAsync(string id, byte[] payload, CancellationToken ct) => Task.FromResult(DeviceProtocol.Encode(Get(id).SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        public Task DeleteAsync(string id, CancellationToken ct) { if (values.Remove(id, out var key)) key.Dispose(); return Task.CompletedTask; }
        private ECDsa Get(string id) => values.TryGetValue(id, out var key) ? key : throw new CryptographicException("Key unavailable.");
        public void Dispose() { foreach (var key in values.Values) key.Dispose(); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock = new();
        public readonly Vault Vault = new();
        public readonly TestKeys Keys = new();
        public readonly Server Server;
        public Fixture() { Server = new(Clock); }
        public AgentCoordinator Create() => new(Vault, new(new HttpClient(Server, false)), Keys, Clock);
        public async Task<AgentCoordinator> Enroll(bool complete = true)
        {
            var agent = Create();
            await agent.ConfigureAsync("https://lab.example", default);
            await agent.RegisterAsync(1, 1, "test", default);
            Server.Status = "Approved";
            if (complete) await agent.TickAsync(default);
            return agent;
        }
        public void Dispose() { Keys.Dispose(); Server.Dispose(); }
    }
    private sealed class Server(Clock clock) : HttpMessageHandler
    {
        public string Status = "Pending";
        public bool Offline, Revoked, LoseClaim, LoseRotation, FailBeforeRotation;
        public int UnauthorizedHeartbeats, Registrations, Claims, Authentications, Rotations;
        public DeviceIdentity Identity = new(Guid.NewGuid(), Guid.NewGuid(), 1, "CLMS.Agent");
        private readonly DeviceProofVerifier verifier = new();
        private readonly Dictionary<Guid, (DeviceChallengeResponse Challenge, string Purpose)> challenges = [];
        private string publicKey = "", replacementKey = "";
        private Guid registration, replacementId;
        private bool claimed;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("Simulated outage.");
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/laboratories")) return Ok(new[] { new RegistrationLaboratory(1, "Lab") });
            if (path == "/api/agent/registrations")
            {
                var body = await Read<RegisterUnitRequest>(request, ct);
                publicKey = body.PublicKey; Registrations++; claimed = false; registration = Guid.NewGuid();
                return Ok(new RegistrationReceipt(registration, "TEST2345", DeviceProtocol.Encode(RandomNumberGenerator.GetBytes(32)), clock.Now.UtcDateTime, clock.Now.UtcDateTime.AddMinutes(10)));
            }
            if (path.EndsWith("/status")) return Ok(new RegistrationState(Status, clock.Now.UtcDateTime, clock.Now.UtcDateTime.AddMinutes(10), claimed, Status == "Approved" ? Identity : null));
            if (path.EndsWith("/completion-challenge")) return Ok(Challenge(DeviceProtocol.Complete, registration));
            if (path.EndsWith("/claim"))
            {
                var body = await Read<ClaimUnitRequest>(request, ct);
                Assert.False(claimed);
                if (!Verify(body.ChallengeId, body.Signature, publicKey)) return Invalid();
                claimed = true; Claims++;
                if (LoseClaim) { LoseClaim = false; throw new HttpRequestException("Simulated lost completion response."); }
                return Ok(Identity);
            }
            if (path.EndsWith("/auth/challenge"))
            {
                var body = await Read<DeviceChallengeRequest>(request, ct);
                if (Revoked || !claimed || body.CredentialId != Identity.CredentialId) return Invalid();
                return Ok(Challenge(DeviceProtocol.Authenticate, Guid.Empty));
            }
            if (path.EndsWith("/auth/token"))
            {
                var body = await Read<DeviceProofRequest>(request, ct);
                if (Revoked || !Verify(body.ChallengeId, body.Signature, publicKey)) return Invalid();
                Authentications++;
                return Ok(new AgentTokens("memory-only-token", clock.Now.UtcDateTime.AddMinutes(15), Identity.ComputerUnitId, Identity.Generation, Identity.CredentialId));
            }
            if (path.EndsWith("/rotation-challenge"))
            {
                var body = await Read<RotationChallengeRequest>(request, ct);
                if (replacementKey != body.PublicKey) { replacementKey = body.PublicKey; replacementId = Guid.NewGuid(); }
                return Ok(Challenge(DeviceProtocol.RotateCurrent, body.OperationId, replacementId, DeviceProtocol.Fingerprint(DeviceProtocol.Decode(replacementKey))));
            }
            if (path.EndsWith("/auth/rotate"))
            {
                var body = await Read<RotationProofRequest>(request, ct);
                if (FailBeforeRotation) { FailBeforeRotation = false; throw new HttpRequestException("Simulated failure before commit."); }
                var pending = challenges[body.ChallengeId].Challenge;
                Assert.True(verifier.Verify(replacementKey, Payload(pending, DeviceProtocol.RotateReplacement), body.ReplacementSignature));
                Assert.True(Verify(body.ChallengeId, body.Signature, publicKey));
                Identity = Identity with { CredentialId = replacementId }; publicKey = replacementKey; Rotations++;
                if (LoseRotation) { LoseRotation = false; throw new HttpRequestException("Simulated lost rotation response."); }
                return Ok(Identity);
            }
            if (path.EndsWith("/heartbeat"))
            {
                if (Revoked || UnauthorizedHeartbeats-- > 0) return Invalid();
                return new(HttpStatusCode.NoContent);
            }
            return new(HttpStatusCode.NotFound);
        }
        private DeviceChallengeResponse Challenge(string purpose, Guid context, Guid? replacement = null, string fingerprint = "")
        {
            var c = new DeviceChallengeResponse(Guid.NewGuid(), DeviceProtocol.Encode(RandomNumberGenerator.GetBytes(32)), Identity.Audience,
                clock.Now.UtcDateTime.AddSeconds(60), Identity.ComputerUnitId, Identity.CredentialId, context, replacement, fingerprint);
            challenges.Add(c.Id, (c, purpose)); return c;
        }
        private bool Verify(Guid id, string signature, string key) => challenges.Remove(id, out var c) && verifier.Verify(key, Payload(c.Challenge, c.Purpose), signature);
        private static byte[] Payload(DeviceChallengeResponse c, string purpose) => DeviceProtocol.Payload(purpose, c.Audience, c.ComputerUnitId, c.CredentialId, c.ContextId, c.Id, c.Nonce, c.ReplacementFingerprint);
        private static async Task<T> Read<T>(HttpRequestMessage request, CancellationToken ct) => (await request.Content!.ReadFromJsonAsync<T>(cancellationToken: ct))!;
        private static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        private static HttpResponseMessage Invalid() => new(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { code = "invalid_credential" }) };
    }
}