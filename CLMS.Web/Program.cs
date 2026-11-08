using System.Text.Json.Serialization;
using CLMS.Application;
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
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;



var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var cookieSecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddOptions<AccountOptions>()
    .Bind(builder.Configuration.GetSection("Onboarding"))
    // Apply after configuration so production cannot opt out of HTTPS through a setting.
    .PostConfigure(options => options.RequireHttps = !isDevelopment);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccountOptions>>().Value);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("GoogleLink", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
        options.LoginPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
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

// TODO Define access policies
builder.Services
    .AddAuthorization(options =>
    {
        // TODO SuperAdmin: replace Admin with the future SuperAdmin role.
        options.AddPolicy("RegisterUsers", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
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
builder.Services.AddScoped<DialogService>();
builder.Services.AddCascadingAuthenticationState();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute(
    "/not-found",
    createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();


app.Run();
