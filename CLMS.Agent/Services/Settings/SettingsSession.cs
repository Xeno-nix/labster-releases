using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CLMS.Agent.Core;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

/// <summary>Short-lived, memory-only human authentication, separate from workstation identity.</summary>
public sealed class SettingsSession(HttpClient http)
{
    private string? accessToken;
    private string? challenge;
    private DateTimeOffset expires;
    private int generation;
    public string? Server { get; private set; }
    public bool IsUnlocked => accessToken is not null && DateTimeOffset.UtcNow < expires;
    public bool NeedsCode => challenge is not null;

    public void Lock()
    {
        generation++;
        accessToken = challenge = null;
        expires = default;
    }

    public async Task SignInAsync(string server, string email, string password)
    {
        Lock();
        Server = AgentApi.ValidateAddress(server).AbsoluteUri;
        var version = generation;
        using var response = await SendAsync("api/auth/login", new AuthenticationRequestDto(email.Trim(), password));
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var receipt = await response.Content.ReadFromJsonAsync<EmailLoginReceipt>();
            if (version != generation) throw new UnauthorizedAccessException("Sign in again.");
            challenge = receipt?.ChallengeCredential ?? throw new HttpRequestException("Invalid verification response.");
            return;
        }
        await CompleteAsync(response, version);
    }

    public async Task VerifyAsync(string code)
    {
        var version = generation;
        using var response = await SendAsync("api/auth/two-factor/verify",
            new VerifyLoginCodeDto(challenge ?? throw new UnauthorizedAccessException("Sign in again."), code.Trim()));
        await CompleteAsync(response, version);
    }

    public async Task ResendAsync()
    {
        var version = generation;
        using var response = await SendAsync("api/auth/two-factor/resend",
            new ResendLoginCodeDto(challenge ?? throw new UnauthorizedAccessException("Sign in again.")));
        await EnsureSuccess(response);
        var receipt = await response.Content.ReadFromJsonAsync<EmailLoginReceipt>();
        if (version == generation) challenge = receipt?.ChallengeCredential;
    }

    public async Task RequireAccessAsync()
    {
        if (!IsUnlocked) { Lock(); throw new UnauthorizedAccessException("Sign in to unlock agent settings."); }
        var version = generation;
        try
        {
            using var response = await SendAsync("api/agent/settings/access", token: accessToken);
            await EnsureSuccess(response);
            if (version != generation || !IsUnlocked) throw new UnauthorizedAccessException("Sign in again.");
        }
        catch { Lock(); throw; } // Offline, expired, disabled and demoted accounts all fail closed.
    }

    private async Task CompleteAsync(HttpResponseMessage response, int version)
    {
        await EnsureSuccess(response);
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokensDto>()
            ?? throw new HttpRequestException("Invalid sign-in response.");
        // The desktop never refreshes human sessions. Revoke the unused refresh credential immediately.
        try { using var logout = await SendAsync("api/auth/logout", tokens.RefreshToken); }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        if (version != generation) throw new UnauthorizedAccessException("Sign in again.");
        // Never expose an unlocked state while the role check is still in flight.
        using var authorization = await SendAsync("api/agent/settings/access", token: tokens.AccessToken);
        try { await EnsureSuccess(authorization); }
        catch { Lock(); throw; }
        if (version != generation) throw new UnauthorizedAccessException("Sign in again.");
        accessToken = tokens.AccessToken;
        expires = DateTimeOffset.UtcNow.AddMinutes(10);
        challenge = null;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
            new Uri(AgentApi.ValidateAddress(Server ?? ""), path));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Contains("password_change_required", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Change your initial password in the web app, then sign in here.");
        throw new UnauthorizedAccessException(response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Only Admin and Super Admin accounts can open agent settings.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait before trying again.",
            _ => "Sign-in or verification failed. Check your credentials and code, or try again later."
        });
    }
}
