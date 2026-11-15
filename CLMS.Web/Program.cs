using System.Text.Json.Serialization;
using CLMS.Application;
using CLMS.Web.Components;
using CLMS.Application.Services;
using CLMS.Infrastructure.Services;
using CLMS.Infrastructure;
using CLMS.Infrastructure.Auth;
using System.Text;
using CLMS.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CLMS.Infrastructure.DatabaseMaintenance;
using CLMS.Shared.Dtos;
using CLMS.Web.Hubs;
using CLMS.Web.Services.AgentConnections;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddRazorComponents()
        .AddInteractiveServerComponents();

// Api stuff, and JSON string <-> enum converters
// from http requests
builder.Services
    .AddControllers()
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
            CookieAuthenticationDefaults.AuthenticationScheme;

        options.DefaultSignInScheme =
            CookieAuthenticationDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
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
                    builder.Configuration["Jwt:SecretKey"]!)
            )
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "CLMS.Auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";

        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services
    .AddAuthorization(options =>
    {
        options.AddPolicy("JwtPolicy", policy =>
        {
            policy.AuthenticationSchemes.Add("jwt");
            policy.RequireAuthenticatedUser();
        });

        options.AddPolicy("CookiePolicy", policy =>
        {
            policy.AuthenticationSchemes.Add("cookie");
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}

// Hub clients must receive protocol status codes, not an HTML page re-execution.
// Re-executing a failed negotiate POST against a Razor page can mask a 401 as a 400.
app.UseWhen(context => !context.Request.Path.StartsWithSegments(AgentConnectionProtocol.HubPath)
    && !context.Request.Path.StartsWithSegments(ScreenCastProtocol.FrameRoutePrefix), branch =>
    branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
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
