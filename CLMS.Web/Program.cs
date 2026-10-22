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
using CLMS.Web.Auth;
using CLMS.Web.Endpoints;
using CLMS.Web.Hosted;
using Microsoft.AspNetCore.Authentication;



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
        });        options.AddPolicy("CookiePolicy", policy =>
        {
            policy.AuthenticationSchemes.Add("cookie");
            policy.RequireAuthenticatedUser();
        });

        // Per-unit API-key auth for the lab-PC agents (X-Agent-Key header).
        options.AddPolicy("AgentPolicy", policy =>
        {
            policy.AuthenticationSchemes.Add(AgentApiKeyAuthenticationHandler.SchemeName);
            policy.RequireAuthenticatedUser();
        });
        
    });

// Agent API-key scheme (hash lookup against computer_units.agent_api_key_hash).
builder.Services
    .AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, AgentApiKeyAuthenticationHandler>(
        AgentApiKeyAuthenticationHandler.SchemeName, null);

// Orphaned-session cleanup: closes logs for units that stopped heartbeating.
builder.Services.AddHostedService<UnitLogCleanupWorker>();

// booking daw
builder.Services.AddHttpClient<EmailService>();

// web stuff
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<DialogService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute(
    "/not-found",
    createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapAgentEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();


app.Run();
