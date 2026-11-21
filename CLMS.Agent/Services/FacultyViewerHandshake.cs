using System.Text.Json;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

/// <summary>Waits for the actual viewer script, not an initial blank WebView navigation.</summary>
public static class FacultyViewerHandshake
{
    public static async Task<string> NegotiateAsync(string offer, Func<string, Task<string?>> evaluate,
        CancellationToken cancellationToken)
    {
        using (var loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            loading.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                while (true)
                {
                    loading.Token.ThrowIfCancellationRequested();
                    try
                    {
                        using var ready = Parse(await evaluate("JSON.stringify(window.facultyViewerReady === true)").WaitAsync(loading.Token));
                        if (ready.RootElement.ValueKind == JsonValueKind.True) break;
                    }
                    catch (Exception) when (!loading.IsCancellationRequested) { /* Native handler/document may still be initializing. */ }
                    await Task.Delay(100, loading.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new FacultyViewerException("page_load_timeout"); }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // A synchronous wrapper avoids returning a JavaScript Promise to WKWebView/WebView2.
            using var started = Parse(await EvaluateAsync($"JSON.stringify(window.startFacultyOffer({JsonSerializer.Serialize(offer)}))", evaluate, deadline.Token));
            if (started.RootElement.ValueKind != JsonValueKind.True) throw new FacultyViewerException("script_start_failed");
            while (true)
            {
                using var result = Parse(await EvaluateAsync("JSON.stringify(window.broadcastResult || null)", evaluate, deadline.Token));
                if (result.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (result.RootElement.TryGetProperty("error", out var error))
                        throw new FacultyViewerException(error.ValueKind == JsonValueKind.String ? error.GetString() : null);
                    if (result.RootElement.TryGetProperty("sdp", out var sdp) && sdp.ValueKind == JsonValueKind.String)
                    {
                        var answer = sdp.GetString();
                        if (answer is not null && answer.StartsWith("v=0", StringComparison.Ordinal) && answer.Length <= FacultyStreamProtocol.MaximumSdpLength)
                            return answer;
                        throw new FacultyViewerException("invalid_answer");
                    }
                }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new FacultyViewerException("answer_timeout"); }
    }

    private static JsonDocument Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return JsonDocument.Parse("null");
        if (value.Length > FacultyStreamProtocol.MaximumSdpLength * 8) throw new FacultyViewerException("bridge_response_invalid");
        for (var depth = 0; depth < 4; depth++)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(value); }
            catch (JsonException)
            {
                // MAUI trims native string-result quotes but leaves their JSON escapes intact.
                // Restore only that string layer, then parse the contained JSON normally.
                try { value = JsonSerializer.Deserialize<string>("\"" + value + "\""); }
                catch (JsonException) { throw new FacultyViewerException("bridge_response_invalid"); }
                if (string.IsNullOrEmpty(value)) return JsonDocument.Parse("null");
                continue;
            }
            if (document.RootElement.ValueKind != JsonValueKind.String) return document;
            value = document.RootElement.GetString();
            document.Dispose();
            if (string.IsNullOrEmpty(value)) return JsonDocument.Parse("null");
        }
        throw new FacultyViewerException("bridge_response_invalid");
    }

    private static async Task<string?> EvaluateAsync(string script, Func<string, Task<string?>> evaluate, CancellationToken token)
    {
        try { return await evaluate(script).WaitAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch { throw new FacultyViewerException("script_execution_failed"); }
    }
}

public sealed class FacultyViewerException : Exception
{
    public string Code { get; }
    public FacultyViewerException(string? code) : base("Faculty viewer setup failed.") => Code = code switch
    {
        "page_load_timeout" or "script_start_failed" or "unsupported_webrtc" or "offer_rejected"
            or "answer_failed" or "ice_timeout" or "answer_timeout" or "invalid_answer" or "signaling_failed"
            or "bridge_response_invalid" or "script_execution_failed" or "viewer_start_failed" => code,
        _ => "negotiation_failed"
    };
    public string UserMessage => Code switch
    {
        "page_load_timeout" => "The viewer page did not finish loading. Restart the agent and check that the WebView runtime is available.",
        "unsupported_webrtc" => "The workstation's WebView does not support WebRTC. Update the WebView runtime and try again.",
        "ice_timeout" or "answer_timeout" => "The workstation could not prepare the video connection in time. Check its network connection and ask the presenter to restart sharing.",
        "offer_rejected" or "answer_failed" or "invalid_answer" => "The video connection could not be negotiated. Update both CLMS.Web and the workstation agent, then restart sharing.",
        "signaling_failed" => "The live-stream connection could not be confirmed with the server. Check the agent connection and monitoring session, then ask the presenter to restart sharing.",
        "bridge_response_invalid" => "The viewer returned an unreadable connection response. Install the latest workstation agent and restart sharing.",
        "script_execution_failed" => "The WebView could not run the connection script. Restart the agent and check its WebView runtime.",
        "viewer_start_failed" => "The workstation could not initialize the viewer. Restart the agent and check its WebView runtime.",
        _ => "The live stream could not start. Restart the agent and ask the presenter to restart sharing."
    };
}
