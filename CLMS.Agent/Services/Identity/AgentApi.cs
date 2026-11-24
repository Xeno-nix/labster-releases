using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Core;

public sealed class AgentApi(HttpClient http)
{
    /// <summary>Accepts only an absolute HTTPS origin without embedded credentials, query, fragment, or API path.</summary>
    public static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("Enter an HTTPS server origin, for example https://clms.lab:7443.");
        return uri;
    }
    /// <summary>Fetches available laboratory IDs and names without requiring permanent device credentials.</summary>
    public Task<IReadOnlyList<RegistrationLaboratory>> Laboratories(string server, CancellationToken ct) =>
        Send<IReadOnlyList<RegistrationLaboratory>>(server, "api/agent/laboratories", null, null, ct);
    /// <summary>Creates a pending registration and receives its private receipt and human-readable verification code.</summary>
    public Task<RegistrationReceipt> Register(string server, RegisterUnitRequest request, CancellationToken ct) =>
        Send<RegistrationReceipt>(server, "api/agent/registrations", request, null, ct);
    /// <summary>Polls the registration lifecycle using its private secret; the displayed code is not an authentication credential.</summary>
    public Task<RegistrationState> Status(string server, RegistrationReceipt registration, CancellationToken ct) =>
        Send<RegistrationState>(server, $"api/agent/registrations/{registration.Id}/status", new RegistrationSecret(registration.Secret), null, ct);
    /// <summary>Obtains a key-bound registration completion challenge using the private receipt.</summary>
    public Task<DeviceChallengeResponse> CompletionChallenge(string server, RegistrationReceipt registration, CancellationToken ct) =>
        Send<DeviceChallengeResponse>(server, $"api/agent/registrations/{registration.Id}/completion-challenge", new RegistrationSecret(registration.Secret), null, ct);
    /// <summary>Activates an approved key once after proving possession.</summary>
    public Task<DeviceIdentity> Claim(string server, RegistrationReceipt registration, Guid challenge, string signature, CancellationToken ct) =>
        Send<DeviceIdentity>(server, $"api/agent/registrations/{registration.Id}/claim", new ClaimUnitRequest(registration.Secret, challenge, signature), null, ct);
    /// <summary>Obtains a fresh single-use authentication challenge.</summary>
    public Task<DeviceChallengeResponse> Challenge(string server, DeviceIdentity identity, CancellationToken ct) =>
        Send<DeviceChallengeResponse>(server, "api/agent/auth/challenge", new DeviceChallengeRequest(identity.ComputerUnitId, identity.CredentialId), null, ct);
    /// <summary>Exchanges a private-key proof for a memory-only access token.</summary>
    public Task<AgentTokens> Authenticate(string server, DeviceProofRequest proof, CancellationToken ct) =>
        Send<AgentTokens>(server, "api/agent/auth/token", proof, null, ct);
    /// <summary>Proposes replacement public material using the current authorized access token.</summary>
    public Task<DeviceChallengeResponse> RotationChallenge(string server, string access, RotationChallengeRequest request, CancellationToken ct) =>
        Send<DeviceChallengeResponse>(server, "api/agent/auth/rotation-challenge", request, access, ct);
    /// <summary>Completes rotation with separate possession proofs from the current and replacement keys.</summary>
    public Task<DeviceIdentity> Rotate(string server, RotationProofRequest proof, CancellationToken ct) =>
        Send<DeviceIdentity>(server, "api/agent/auth/rotate", proof, null, ct);
    /// <summary>Reports availability using an agent access token; the server supplies the authoritative timestamp and source IP.</summary>
    public Task<bool> Heartbeat(string server, string accessToken, CancellationToken ct) =>
        Send<bool>(server, "api/agent/heartbeat", new { }, accessToken, ct);

    internal Task<T> Kiosk<T>(string server, string action, object payload, string token, CancellationToken ct) =>
        Send<T>(server, "api/agent/kiosk/" + action, payload, token, ct);

    /// <summary>Sends a bounded HTTPS JSON request with a timeout covering headers and body. Converts unsuccessful responses to structured errors without exposing their bodies in UI messages.</summary>
    private async Task<T> Send<T>(string server, string path, object? body, string? access, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        ct = deadline.Token; // Include response-body reads in the timeout, not just headers.
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(ValidateAddress(server), path));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (access is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await response.Content.LoadIntoBufferAsync(1024 * 1024, ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = "request_failed";
            // Never propagate server bodies into logs or local notifications.
            try
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (json.RootElement.TryGetProperty("code", out var property)) code = property.GetString() ?? code;
            }
            catch (JsonException) { }
            throw new AgentApiException((int)response.StatusCode, code,
                response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow));
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return (T)(object)true;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new HttpRequestException("Empty server response.");
    }
}
