using System.Security.Cryptography;
using CLMS.Shared.Dtos;
using CLMS.Shared.Security;

namespace CLMS.Agent.Core;

/// <summary>The service owns enrollment, persistent key references, and serialized JWT acquisition.</summary>
public sealed class AgentCoordinator(IAgentVault vault, AgentApi api, IDeviceKeyStore keys, TimeProvider? clock = null) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private AgentState state = new();
    private AgentTokens? access; // Deliberately never persisted.
    private bool initialized;
    private int failures;
    private AgentStatus status = new("Starting", "Starting the agent service.");
    public AgentStatus Status => Volatile.Read(ref status);
    public TimeSpan NextDelay { get; private set; } = TimeSpan.FromSeconds(2);

    /// <summary>Loads service metadata under the lifecycle gate.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await InitializeCore(ct); }
        finally { gate.Release(); }
    }
    private async Task InitializeCore(CancellationToken ct)
    {
        if (initialized) return;
        state = await vault.ReadAsync(ct) ?? new();
        initialized = true;
        Publish(state.Phase, state.Phase == "RecoveryRequired"
            ? "Ask an administrator to reset this device and its server assignment before registering again."
            : "Agent service is ready.");
    }

    /// <summary>Changes the HTTPS origin only while no registration or identity is active.</summary>
    public async Task ConfigureAsync(string address, CancellationToken ct)
    {
        var origin = AgentApi.ValidateAddress(address).AbsoluteUri;
        await gate.WaitAsync(ct);
        try
        {
            await InitializeCore(ct);
            if (state.Identity is not null || state.Registration is not null || state.Phase == "RecoveryRequired")
                throw new InvalidOperationException("Use the administrative reset procedure before changing a registered server.");
            await CleanupRetiredKey(ct);
            await Save(new(origin, RetiredKeyId: state.KeyId), ct);
            await CleanupRetiredKey(ct);
            failures = 0;
            Publish("RegistrationRequired", "Select a laboratory and enter the unit number.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Retrieves laboratory choices without exposing service credentials to the UI.</summary>
    public async Task<IReadOnlyList<RegistrationLaboratory>> LaboratoriesAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await InitializeCore(ct); return await api.Laboratories(RequireServer(), ct); }
        finally { gate.Release(); }
    }

    /// <summary>Journals a new key before submitting only its public material to registration.</summary>
    public async Task RegisterAsync(int laboratoryId, int number, string agentVersion, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await InitializeCore(ct);
            if (state.Registration is not null || state.Identity is not null || state.Phase == "RecoveryRequired")
                throw new InvalidOperationException("Registration is active or requires administrator recovery.");
            if (number is < 1 or > 255 || laboratoryId <= 0) throw new ArgumentException("Select a laboratory and unit number 1–255.");
            await CleanupRetiredKey(ct);
            await Save(state with { KeyId = Guid.NewGuid().ToString("N"), RetiredKeyId = state.KeyId }, ct);
            var publicKey = await keys.CreateAsync(state.KeyId!, ct);
            await CleanupRetiredKey(ct);
            var receipt = await api.Register(RequireServer(), new(laboratoryId, number, Environment.MachineName,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription, agentVersion, publicKey), ct);
            await Save(state with { Registration = receipt, Phase = "Pending" }, ct);
            failures = 0;
            Publish("Pending", "Show this code to the laboratory administrator. Waiting for approval.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Retires rejected or expired keys before retrying with fresh material.</summary>
    public async Task RetryAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await InitializeCore(ct);
            if (state.Phase is not ("Rejected" or "Expired" or "RegistrationRequired"))
                throw new InvalidOperationException("The request is active or requires administrator recovery.");
            await Save(new(state.ServerAddress, RetiredKeyId: state.KeyId), ct);
            await CleanupRetiredKey(ct);
            Publish("RegistrationRequired", "Select a laboratory and enter the unit number to retry.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Polls enrollment, recovers rotation, acquires a JWT, and sends a heartbeat with one authentication retry.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await InitializeCore(ct);
            await CleanupRetiredKey(ct);
            if (state.ServerAddress is null || state.Phase is "Rejected" or "Expired" or "RecoveryRequired")
            { NextDelay = TimeSpan.FromSeconds(5); return; }
            if (state.Identity is null && state.Registration is null)
            {
                await api.Laboratories(RequireServer(), ct);
                failures = 0;
                NextDelay = TimeSpan.FromSeconds(30);
                Publish("RegistrationRequired", "Connected securely. Select a laboratory and enter the unit number.");
                return;
            }
            if (state.Registration is not null)
            {
                var result = await api.Status(RequireServer(), state.Registration, ct);
                if (result.Status is "Rejected" or "Expired")
                {
                    await Save(state with { Phase = result.Status, Identity = null }, ct);
                    Publish(result.Status, result.Status == "Rejected" ? "Registration was rejected. Retry with the correct assignment." : "Registration expired. Start a new request.");
                    return;
                }
                if (result.Status != "Approved")
                {
                    failures = 0;
                    NextDelay = TimeSpan.FromSeconds(5);
                    Publish("Pending", "Waiting for administrator approval.");
                    return;
                }
                if (result.Identity is null || state.KeyId is null) throw new CryptographicException();
                await Save(state with { Identity = result.Identity }, ct);
                if (!result.CredentialsClaimed)
                {
                    var challenge = await api.CompletionChallenge(RequireServer(), state.Registration, ct);
                    var signature = await Sign(challenge, DeviceProtocol.Complete, state.KeyId, state.Registration.Id, ct);
                    var identity = await api.Claim(RequireServer(), state.Registration, challenge.Id, signature, ct);
                    RequireIdentity(identity, state.Identity!);
                }
                await Save(state with { Registration = null, Phase = "Connected" }, ct);
            }
            if (state.Rotation is not null) await FinishRotation(ct);
            await EnsureAccess(ct);
            try { await api.Heartbeat(RequireServer(), access!.AccessToken, ct); }
            catch (AgentApiException ex) when (ex.Status == 401)
            {
                access = null;
                await EnsureAccess(ct);
                await api.Heartbeat(RequireServer(), access!.AccessToken, ct);
            }
            failures = 0;
            NextDelay = TimeSpan.FromSeconds(30);
            Publish("Connected", "Connected to the laboratory server.");
        }
        catch (AgentApiException ex) when (ex.Status == 401 || ex.Status == 404 && ex.Code == "registration_unavailable")
        {
            access = null;
            await Save(state with { Phase = "RecoveryRequired" }, ct);
            NextDelay = TimeSpan.FromSeconds(60);
            Publish("RecoveryRequired", "The device identity is unavailable or revoked. Ask an administrator to review or reset registration.");
        }
        catch (AgentApiException ex)
        { Offline(ex.Status == 429 ? "Server request limit reached. Retrying shortly." : "Server is unavailable. Retrying with the stored identity.", ex.RetryAfter); }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException || !initialized && ex is IOException or System.Text.Json.JsonException)
        {
            access = null;
            NextDelay = TimeSpan.FromSeconds(60);
            Publish("StorageError", "The service cannot access its device key or vault. Ask an administrator to repair access; stored identity has been retained.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { Offline("Unable to connect securely. Check the server, network, and certificate trust; retrying."); }
        finally { gate.Release(); }
    }

    /// <summary>Starts or resumes rotation. The IPC caller must have administrator authorization.</summary>
    public async Task RotateKeyAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await InitializeCore(ct);
            if (state.Identity is null || state.Registration is not null || state.Phase == "RecoveryRequired")
                throw new InvalidOperationException("An active registered identity is required for rotation.");
            await CleanupRetiredKey(ct);
            if (state.Rotation is null)
                await Save(state with { Rotation = new(Guid.NewGuid(), Guid.NewGuid().ToString("N")) }, ct);
            await FinishRotation(ct);
            Publish("Connected", "Device key rotated successfully.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Tests the replacement after an ambiguous commit; otherwise proves both keys before switching.</summary>
    private async Task FinishRotation(CancellationToken ct)
    {
        var rotation = state.Rotation!;
        var publicKey = await keys.CreateAsync(rotation.NewKeyId, ct);
        if (rotation.Replacement is not null)
        {
            try
            {
                access = await Authenticate(rotation.Replacement, rotation.NewKeyId, ct);
                await CommitRotation(rotation.Replacement, rotation.NewKeyId, ct);
                return;
            }
            catch (AgentApiException ex) when (ex.Status == 401) { /* The old key may still be current. */ }
        }
        await EnsureAccess(ct);
        DeviceChallengeResponse challenge;
        try { challenge = await api.RotationChallenge(RequireServer(), access!.AccessToken, new(rotation.OperationId, publicKey), ct); }
        catch (AgentApiException ex) when (ex.Status == 401)
        {
            access = null;
            await EnsureAccess(ct);
            challenge = await api.RotationChallenge(RequireServer(), access!.AccessToken, new(rotation.OperationId, publicKey), ct);
        }
        if (challenge.ReplacementCredentialId is not { } replacementId ||
            challenge.ReplacementFingerprint != DeviceProtocol.Fingerprint(DeviceProtocol.Decode(publicKey))) throw new CryptographicException();
        var replacement = state.Identity! with { CredentialId = replacementId };
        await Save(state with { Rotation = rotation with { Replacement = replacement } }, ct);
        var currentProof = await Sign(challenge, DeviceProtocol.RotateCurrent, state.KeyId!, rotation.OperationId, ct);
        var nextProof = await Sign(challenge, DeviceProtocol.RotateReplacement, rotation.NewKeyId, rotation.OperationId, ct);
        var result = await api.Rotate(RequireServer(), new(state.Identity!.ComputerUnitId, state.Identity.CredentialId,
            challenge.Id, currentProof, nextProof), ct);
        RequireIdentity(result, replacement);
        await CommitRotation(result, rotation.NewKeyId, ct);
        access = null;
        await EnsureAccess(ct);
    }
    private async Task CommitRotation(DeviceIdentity identity, string keyId, CancellationToken ct)
    {
        await Save(state with { Identity = identity, KeyId = keyId, Rotation = null, RetiredKeyId = state.KeyId, Phase = "Connected" }, ct);
        await CleanupRetiredKey(ct);
    }
    /// <summary>Shares an unexpired memory-only token with all callers under the lifecycle gate.</summary>
    private async Task EnsureAccess(CancellationToken ct)
    {
        if (access is null || access.AccessExpiresAtUtc <= time.GetUtcNow().UtcDateTime.AddMinutes(1))
            access = await Authenticate(state.Identity!, state.KeyId!, ct);
    }
    private async Task<AgentTokens> Authenticate(DeviceIdentity identity, string keyId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var challenge = await api.Challenge(RequireServer(), identity, ct);
            var signature = await Sign(challenge, DeviceProtocol.Authenticate, keyId, Guid.Empty, ct, identity);
            try
            {
                var result = await api.Authenticate(RequireServer(), new(identity.ComputerUnitId, identity.CredentialId, challenge.Id, signature), ct);
                if (result.ComputerUnitId != identity.ComputerUnitId || result.CredentialId != identity.CredentialId || result.Generation != identity.Generation)
                    throw new CryptographicException();
                return result;
            }
            catch (AgentApiException ex) when (ex.Status == 401 && attempt == 0)
            { /* A challenge may have expired in transit. Retry one fresh challenge before requiring recovery. */ }
        }
    }
    private Task<string> Sign(DeviceChallengeResponse challenge, string purpose, string keyId, Guid context, CancellationToken ct, DeviceIdentity? expected = null)
    {
        expected ??= state.Identity!;
        if (challenge.ComputerUnitId != expected.ComputerUnitId || challenge.CredentialId != expected.CredentialId ||
            challenge.Audience != expected.Audience || challenge.ContextId != context) throw new CryptographicException();
        return keys.SignAsync(keyId, DeviceProtocol.Payload(purpose, expected.Audience, expected.ComputerUnitId,
            expected.CredentialId, context, challenge.Id, challenge.Nonce, challenge.ReplacementFingerprint), ct);
    }
    private static void RequireIdentity(DeviceIdentity actual, DeviceIdentity expected)
    { if (actual != expected) throw new CryptographicException(); }
    /// <summary>Deletes obsolete material only after a durable state transition no longer depends on that key.</summary>
    private async Task CleanupRetiredKey(CancellationToken ct)
    {
        var retired = (state.RetiredKeys ?? []).Concat(state.RetiredKeyId is { } id ? new[] { id } : []).Distinct().ToArray();
        if (retired.Length == 0) return;
        foreach (var key in retired) await keys.DeleteAsync(key, ct);
        await Save(state with { RetiredKeyId = null, RetiredKeys = null }, ct);
    }
    private async Task Save(AgentState next, CancellationToken ct) { await vault.WriteAsync(next, ct); state = next; }
    private string RequireServer() => state.ServerAddress ?? throw new InvalidOperationException("Configure the HTTPS server first.");
    private void Publish(string phase, string message) => Volatile.Write(ref status, new(phase, message, state.ServerAddress,
        state.Registration?.Code, state.Registration?.ExpiresAtUtc, state.Identity?.ComputerUnitId,
        state.Identity is null && state.Registration is null && state.Phase != "RecoveryRequired",
        state.ServerAddress is not null && state.Identity is null && state.Registration is null && state.Phase != "RecoveryRequired",
        state.Phase is "Rejected" or "Expired"));
    private void Offline(string message, TimeSpan? retry = null)
    {
        NextDelay = retry.HasValue ? TimeSpan.FromSeconds(Math.Clamp(retry.Value.TotalSeconds, 2, 300)) : ReconnectDelay(failures++);
        Publish("Offline", message);
    }
    /// <summary>Jitters reconnect attempts with exponential delays capped at sixty seconds.</summary>
    public static TimeSpan ReconnectDelay(int failures) => TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, Math.Clamp(failures, 0, 5))) * (0.8 + Random.Shared.NextDouble() * 0.2));
    public void Dispose() => gate.Dispose();
}
