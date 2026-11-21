using System.Text.Json.Serialization;
using CLMS.Application;
using CLMS.Application.Authorization;
using CLMS.Web.Components;
using CLMS.Application.Services;
using CLMS.Infrastructure.Services;
using CLMS.Infrastructure;
using CLMS.Infrastructure.Auth;
using System.Text;
using CLMS.Web.Authentication;
using CLMS.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CLMS.Infrastructure.DatabaseMaintenance;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.AgentConnections;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;



var builder = WebApplication.CreateBuilder(args);
// Reset links carry bearer credentials. Prevent request URL and action-argument logging.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", LogLevel.Information);
var isDevelopment = builder.Environment.IsDevelopment();
var cookieSecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PrivilegeAuthorizationHandler>();
builder.Services.AddSingleton<AuthorizationChanges>();
builder.Services.AddSingleton<CLMS.Application.Authorization.IAuthorizationChanges>(sp => sp.GetRequiredService<AuthorizationChanges>());
builder.Services.AddScoped<ApplicationMonitoringService>();
builder.Services.AddScoped<ApplicationIconStorage>();
builder.Services.AddScoped<ManualDatabaseBackups>();
builder.Services.AddOptions<CLMS.Application.Monitoring.AbnormalActivityOptions>()
    .Bind(builder.Configuration.GetSection("Monitoring:AbnormalActivity"))
    .Validate(options => options.RetentionDays is >= 1 and <= 3650, "Activity retention must be between 1 and 3650 days.")
    .ValidateOnStart();
builder.Services.AddOptions<AccountOptions>()
    .Bind(builder.Configuration.GetSection("Onboarding"))
    // Apply after configuration so production cannot opt out of HTTPS through a setting.
    .PostConfigure(options => options.RequireHttps = !isDevelopment);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccountOptions>>().Value);
builder.Services.AddHostedService<DeviceChallengeCleanup>();
builder.Services.AddHostedService<PasswordRecoveryWorker>();
builder.Services.AddScoped<HumanSessionValidator>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/agent")
            ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    options.OnRejected = PasswordRecoveryRateLimit.OnRejectedAsync;
    foreach (var (name, limit) in new[] { ("AgentDiscovery", 30), ("AgentRegistration", 5),
                 ("AgentPolling", 30), ("AgentClaim", 10), ("AgentAuthentication", 30), ("AgentHeartbeat", 10) })
    {
        var permits = builder.Configuration.GetValue<int?>($"AgentRateLimits:{name}") ?? limit;
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" +
                (name is "AgentPolling" or "AgentClaim" ? context.Request.RouteValues["id"]?.ToString() : ""),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }
    options.AddPolicy("GoogleLink", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("PasswordRecovery", PasswordRecoveryRateLimit.ForClient);
});

// Add services to the container.
builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddRazorComponents()
        .AddInteractiveServerComponents();

// Api stuff, and JSON string <-> enum converters
// from http requests
builder.Services
    // MVC's antiforgery attributes require ViewFeatures services, including for the small account forms.
    .AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

// For JWT Options Configuration (Grabs config from user secrets)
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(AgentTokenService.IsValidSigningConfiguration,
        "JWT issuer/audience and an HS256/HS384/HS512 signing key of at least 32/48/64 bytes are required.")
    .ValidateOnStart();
builder.Services.AddOptions<AgentAuthOptions>().Bind(builder.Configuration.GetSection("AgentAuth"))
    .Validate(o => o.AccessMinutes is >= 1 and <= 15 &&
        !string.IsNullOrWhiteSpace(o.Audience) && o.Audience.Length <= 200 && !o.Audience.Any(char.IsControl) && o.Audience != builder.Configuration["Jwt:Audience"], "Invalid agent token settings.")
    .ValidateOnStart();

// authentication && authorization
builder.Services
    .AddAuthentication(options =>
    {
        // Used by Blazor/UI
        options.DefaultAuthenticateScheme =
            AuthenticationSchemes.Cookie;

        options.DefaultSignInScheme =
            AuthenticationSchemes.Cookie;

        options.DefaultChallengeScheme =
            AuthenticationSchemes.Cookie;
    })
    .AddJwtBearer(AuthenticationSchemes.Bearer, o =>
    {
        o.Events = new JwtBearerEvents { OnTokenValidated = HumanSessionValidator.ValidateBearerAsync };
        
        o.TokenValidationParameters = new TokenValidationParameters()
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            
            ValidateLifetime = true,
            
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    builder.Configuration["Jwt:Key"]!)
            )
        };
    })
    .AddCookie(AuthenticationSchemes.Cookie, options =>
    {
        options.Cookie.Name = "CLMS.Auth";
        options.Events.OnValidatePrincipal = HumanSessionValidator.ValidateCookieAsync;
        options.LoginPath = "/login";
        options.Cookie.Path = "/";
        options.AccessDeniedPath = "/access-denied";
        
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddCookie(AuthenticationSchemes.PasswordChange, options =>
    {
        options.Cookie.Name = "CLMS.PasswordChange";
        options.Events.OnValidatePrincipal = HumanSessionValidator.ValidateCookieAsync;
        options.LoginPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.SlidingExpiration = false;
    })
    .AddCookie(AuthenticationSchemes.TwoFactor, options =>
    {
        options.Cookie.Name = "CLMS.TwoFactor";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    })
    .AddCookie(AuthenticationSchemes.ExternalCookie, options =>
    {
        options.Cookie.Name = "CLMS.External";

        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;

        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddGoogle(AuthenticationSchemes.Google, options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
        options.SignInScheme = AuthenticationSchemes.ExternalCookie;
        options.CallbackPath = new PathString("/signin-google");
        options.CorrelationCookie.SecurePolicy = cookieSecurePolicy;
        // Google's top-level GET callback supports Lax; SameSite=None requires HTTPS in browsers.
        if (isDevelopment) options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        
        options.Scope.Add("email"); options.Scope.Add("profile");
        
        options.SaveTokens = false;
        options.Events.OnCreatingTicket = context =>
        {
            // Use Google's authenticated user-info response, not browser-supplied form values.
            if (context.User.TryGetProperty("hd", out var domain))
                context.Identity!.AddClaim(new System.Security.Claims.Claim("google:hd", domain.GetString() ?? ""));
            if (context.User.TryGetProperty("verified_email", out var verified) || context.User.TryGetProperty("email_verified", out verified))
                context.Identity!.AddClaim(new System.Security.Claims.Claim("google:email_verified", verified.ToString()));
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = async context =>
        {
            context.HandleResponse();
            await context.HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            context.Response.Redirect("/login?error=google_verification_failed");
        };
    })
    ;

// Preserve existing authentication policies alongside operational privilege policies.
builder.Services
    .AddAuthorization(options =>
    {
        AgentAuthentication.AddPolicies(options);
        options.AddPolicy("JwtPolicy", policy =>
        {
            policy.AuthenticationSchemes.Add(AuthenticationSchemes.Bearer);
            policy.RequireAuthenticatedUser();
        });
        
        options.AddPolicy("CookiePolicy", policy =>
        {
            policy.AuthenticationSchemes.Add(AuthenticationSchemes.Cookie);
            policy.RequireAuthenticatedUser();
        });
    });

// booking daw
builder.Services.AddHttpClient<EmailService>();

// web stuff
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<CLMS.Web.Services.ScreenCasting.LabScreenCastService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<CLMS.Web.Services.ScreenCasting.LabScreenCastService>());
builder.Services.AddScoped<DialogService>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, HumanRevalidatingStateProvider>();

builder.Services.AddOptions<DatabaseBackupOptions>()
    .Bind(builder.Configuration.GetSection("DatabaseBackup"))
    .PostConfigure(options => options.Directory = Path.GetFullPath(options.Directory, builder.Environment.ContentRootPath))
    .Validate(options => options.HourPhilippineTime is >= 0 and <= 23 && options.KeepLast >= 2 && options.TimeoutMinutes > 0,
        "Backup hour must be 0–23, retention at least 2, and timeout positive.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DumpExecutable) && !string.IsNullOrWhiteSpace(options.ClientExecutable),
        "Database backup executable paths are required.")
    .Validate(options =>
    {
        var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return !options.Directory.Equals(webRoot, comparison) &&
            !options.Directory.StartsWith(Path.TrimEndingDirectorySeparator(webRoot) + Path.DirectorySeparatorChar, comparison);
    },
        "Database backups must be stored outside wwwroot.")
    .ValidateOnStart();
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddHostedService<DatabaseBackupWorker>();

builder.Services.AddAgentConnections(builder.Configuration);
builder.Services.AddSingleton<WorkstationSessionGrants>();

var app = builder.Build();
if (args.Contains("--provision-superadmin", StringComparer.Ordinal))
{
    var index = Array.IndexOf(args, "--user-id");
    if (index < 0 || index + 1 >= args.Length || !Guid.TryParse(args[index + 1], out var userId))
        throw new ArgumentException("Use --provision-superadmin --user-id <existing-admin-guid>.");
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CLMS.Application.Authorization.IAuthorizationRepository>().ProvisionSuperAdminAsync(userId);
    app.Logger.LogInformation("Initial SuperAdmin provisioned. Sign in again through the normal authentication flow.");
    return;
}
// Runtime uploads are not included in the build-time static asset manifest.
var applicationIconDirectory = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "application-icons");
Directory.CreateDirectory(applicationIconDirectory);
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/application-icons",
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(applicationIconDirectory)
});
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (!isDevelopment && !context.Request.IsHttps && (path.StartsWithSegments("/auth") || path.StartsWithSegments("/account") ||
        path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/forgot-password") || path.StartsWithSegments("/reset-password")))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("HTTPS is required.");
        return;
    }
    await next(context);
});
app.UseMiddleware<UnitRegistryMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), branch => branch.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true));

    app.UseHsts();
    app.UseHttpsRedirection();
}

// Preserve protocol status codes for all APIs and the device hub.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api")
    && !context.Request.Path.StartsWithSegments(AgentConnectionProtocol.HubPath), branch =>
    branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapGet("/logbook/export", async (HttpContext context, LogbookService logbook, string? search, string? filter, int? lab) =>
{
    var csv = await logbook.ExportCsvAsync(new CLMS.Application.DTOs.Logbook.LogbookQueryDTO
    {
        Search = search,
        Filter = filter,
        LaboratoryId = lab
    });
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "logbook.csv");
}).RequireAuthorization(Privileges.Prefix + Privileges.LogbookExport);
app.MapGet("/admin/applications", (HttpRequest request) =>
    Results.LocalRedirect("/unit-monitoring/applications" + request.QueryString))
    .RequireAuthorization(Privileges.Prefix + Privileges.MonitoringConfigure);
app.MapGet("/admin/unit-registry", (HttpRequest request) =>
    Results.LocalRedirect("/unit-monitoring/unit-registry" + request.QueryString))
    .RequireAuthorization(Privileges.Prefix + Privileges.UnitsManage);
app.MapHub<AgentConnectionHub>(AgentConnectionProtocol.HubPath,
    options =>
    {
        options.CloseOnAuthenticationExpiration = true;
        options.ApplicationMaxBufferSize = ScreenCastProtocol.MaximumHubMessageBytes;
    }).DisableAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();


app.Run();
