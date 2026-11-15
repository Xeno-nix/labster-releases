using System.Threading.RateLimiting;
using CLMS.Web.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using SecurityPage = CLMS.Web.Components.Pages.Account.SecurityForm;

namespace CLMS.Web.Authentication;

/// <summary>Keeps browser recovery throttling on the normal form, with authoritative retry timing.</summary>
public static class PasswordRecoveryRateLimit
{
    public static RateLimitPartition<string> ForClient(HttpContext context) => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 });

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsPost(http.Request.Method) && http.Request.Path.Equals("/forgot-password"))
        {
            var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(15);
            var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
            var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            http.Response.Headers["X-Frame-Options"] = "DENY";
            var email = "";
            // Normal forms are small. Do not parse unbounded/chunked rejected bodies just to echo an address.
            if (http.Request.HasFormContentType && http.Request.ContentLength is > 0 and <= 4096)
            {
                try
                {
                    var value = (await http.Request.ReadFormAsync(ct))["email"].ToString().Trim();
                    if (value.Length <= 100) email = value;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException) { }
            }
            await new RazorComponentResult<SecurityPage>(new
            {
                Model = new SecurityFormModel("forgot", Message: "Too many reset-link requests. Wait for the timer before requesting another link.")
                { Email = email, RecoveryRetryAt = now.AddSeconds(seconds), RecoveryServerTime = now }
            }) { PreventStreamingRendering = true }.ExecuteAsync(http);
            return;
        }
        http.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Too many requests.",
            extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" }).ExecuteAsync(http);
    }
}
