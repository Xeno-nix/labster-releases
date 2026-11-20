using System.Text.Json;
using CLMS.Agent.Services;
using Xunit;

namespace CLMS.Tests;

public sealed class FacultyViewerHandshakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlankNavigationCannotStartNegotiationUntilViewerScriptIsReady(bool doubleEncoded)
    {
        var probes = 0;
        var started = false;
        const string answer = "v=0\r\ns=answer";
        string Encode(string value) => doubleEncoded ? JsonSerializer.Serialize(value) : value;
        Task<string?> Evaluate(string script)
        {
            if (script.Contains("facultyViewerReady")) return Task.FromResult<string?>(Encode(++probes < 3 ? "false" : "true"));
            if (script.Contains("startFacultyOffer"))
            {
                Assert.Equal(3, probes);
                Assert.Contains("JSON.stringify(window.startFacultyOffer(", script);
                started = true;
                return Task.FromResult<string?>(Encode("true"));
            }
            Assert.True(started);
            return Task.FromResult<string?>(Encode(JsonSerializer.Serialize(new { sdp = answer })));
        }
        Assert.Equal(answer, await FacultyViewerHandshake.NegotiateAsync("v=0\r\ns=offer", Evaluate, default));
    }

    [Fact]
    public async Task ClosingDuringPageLoadCancelsWithoutStartingNegotiation()
    {
        using var cancel = new CancellationTokenSource();
        Task<string?> Evaluate(string script)
        {
            Assert.Contains("facultyViewerReady", script);
            cancel.Cancel();
            return Task.FromResult<string?>("false");
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FacultyViewerHandshake.NegotiateAsync("v=0", Evaluate, cancel.Token));
    }

    [Fact]
    public async Task MauiTrimmedJavaScriptStringPreservesEscapedSdp()
    {
        const string answer = "v=0\r\ns=faculty \"screen\"\r\na=ice-ufrag:example\r\n";
        Task<string?> Evaluate(string script)
        {
            if (!script.Contains("broadcastResult")) return Task.FromResult<string?>("true");
            var payload = JsonSerializer.Serialize(new { sdp = answer });
            // MAUI's EvaluateJavaScriptAsync trims the outer JSON quotes, without unescaping the content.
            return Task.FromResult<string?>(JsonSerializer.Serialize(payload).Trim('"'));
        }
        Assert.Equal(answer, await FacultyViewerHandshake.NegotiateAsync("v=0\r\ns=offer", Evaluate, default));
    }

    [Fact]
    public async Task MauiTrimmedErrorResponseKeepsItsSpecificDiagnostic()
    {
        Task<string?> Evaluate(string script) => Task.FromResult<string?>(script.Contains("broadcastResult")
            ? JsonSerializer.Serialize(JsonSerializer.Serialize(new { error = "unsupported_webrtc" })).Trim('"') : "true");
        var error = await Assert.ThrowsAsync<FacultyViewerException>(() => FacultyViewerHandshake.NegotiateAsync("v=0", Evaluate, default));
        Assert.Equal("unsupported_webrtc", error.Code);
    }

    [Fact]
    public async Task InvalidBridgeResponseHasAnActionableDiagnostic()
    {
        Task<string?> Evaluate(string script) => Task.FromResult<string?>(script.Contains("broadcastResult") ? "broken-response" : "true");
        var error = await Assert.ThrowsAsync<FacultyViewerException>(() => FacultyViewerHandshake.NegotiateAsync("v=0", Evaluate, default));
        Assert.Equal("bridge_response_invalid", error.Code);
    }

    [Fact]
    public async Task NativeScriptFailureDoesNotExposeExceptionDetails()
    {
        Task<string?> Evaluate(string script) => script.Contains("facultyViewerReady") ? Task.FromResult<string?>("true")
            : throw new InvalidOperationException("private native exception data");
        var error = await Assert.ThrowsAsync<FacultyViewerException>(() => FacultyViewerHandshake.NegotiateAsync("v=0", Evaluate, default));
        Assert.Equal("script_execution_failed", error.Code);
        Assert.DoesNotContain("private native exception data", error.ToString());
    }

    [Theory]
    [InlineData("unsupported_webrtc")]
    [InlineData("offer_rejected")]
    [InlineData("ice_timeout")]
    public async Task SetupFailuresReturnSafeDiagnosticCodes(string code)
    {
        Task<string?> Evaluate(string script) => Task.FromResult<string?>(script.Contains("broadcastResult")
            ? JsonSerializer.Serialize(new { error = code }) : "true");
        var error = await Assert.ThrowsAsync<FacultyViewerException>(() => FacultyViewerHandshake.NegotiateAsync("v=0", Evaluate, default));
        Assert.Equal(code, error.Code);
        Assert.NotEmpty(error.UserMessage);
    }

    [Fact]
    public async Task UnknownJavaScriptErrorTextIsNotExposedInDiagnostics()
    {
        const string privateText = "private connection details must not be displayed";
        Task<string?> Evaluate(string script) => Task.FromResult<string?>(script.Contains("broadcastResult")
            ? JsonSerializer.Serialize(new { error = privateText }) : "true");
        var error = await Assert.ThrowsAsync<FacultyViewerException>(() => FacultyViewerHandshake.NegotiateAsync("v=0", Evaluate, default));
        Assert.Equal("negotiation_failed", error.Code);
        Assert.DoesNotContain(privateText, error.ToString());
        Assert.DoesNotContain(privateText, error.UserMessage);
    }
}
