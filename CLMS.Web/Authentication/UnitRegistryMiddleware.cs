using System.Data.Common;
using CLMS.Application.UnitRegistry;

namespace CLMS.Web.Authentication;

public sealed class UnitRegistryMiddleware(RequestDelegate next, ILogger<UnitRegistryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/agent") &&
            !context.Request.Path.StartsWithSegments("/api/admin") &&
            !context.Request.Path.StartsWithSegments("/hubs/agents"))
        {
            await next(context);
            return;
        }
        // Never redirect a credential-bearing HTTP request to HTTPS.
        if (!context.Request.IsHttps)
        {
            await Error(context, 400, "https_required", "HTTPS is required.");
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        try { await next(context); }
        catch (RegistryException ex)
        {
            if (ex.StatusCode == 401) logger.LogInformation("Agent authentication denied. Trace {TraceId}", context.TraceIdentifier);
            if (ex.StatusCode == 429) context.Response.Headers.RetryAfter = "60";
            await Error(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (UnauthorizedAccessException) { await Error(context, 403, "administrator_required", "An active administrator is required."); }
        catch (DbException)
        {
            // Do not log exception details: connectors may include connection or parameter information.
            logger.LogWarning("Unit registry database operation failed. Trace {TraceId}", context.TraceIdentifier);
            await Error(context, 503, "registry_unavailable", "The registry is temporarily unavailable.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError("Unit registry operation failed ({ExceptionType}). Trace {TraceId}", ex.GetType().Name, context.TraceIdentifier);
            await Error(context, 500, "registry_error", "The registry operation could not be completed.");
        }
    }
    private static Task Error(HttpContext c, int status, string code, string message) => Results.Problem(
        statusCode: status, title: message, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(c);
}
