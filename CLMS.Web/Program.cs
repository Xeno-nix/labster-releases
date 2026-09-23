using System.Text.Json.Serialization;
using CLMS.Application;
using CLMS.Web.Components;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Services;
using CLMS.Infrastructure.Repositories;
using CLMS.Infrastructure.Services;
using CLMS.Infrastructure;
using CLMS.Infrastructure.Auth;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.Cookies;
using CLMS.Application;

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

// Database connection — shared by every repository below (booking + logging), each of
// which opens its own MySqlConnection per call rather than going through a shared
// DbContext or connection factory.
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Register infrastructure and application services
builder.Services.AddInfrastructureServices(connectionString);
builder.Services.AddApplication();

// Booking module — Application Services
builder.Services.AddScoped<IClassScheduleService, ClassScheduleService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ILaboratoryService, LaboratoryService>();
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<IAcademicTermService, AcademicTermService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddHttpClient<IEmailService, EmailService>();

// Booking module — Repositories
builder.Services.AddScoped<IClassScheduleRepository>(
    _ => new ClassScheduleRepository(connectionString));
builder.Services.AddScoped<IBookingRepository>(
    _ => new BookingRepository(connectionString));
builder.Services.AddScoped<ILaboratoryRepository>(
    _ => new LaboratoryRepository(connectionString));
builder.Services.AddScoped<ICourseRepository>(
    _ => new CourseRepository(connectionString));
builder.Services.AddScoped<IAcademicTermRepository>(
    _ => new AcademicTermRepository(connectionString));
builder.Services.AddScoped<IUserRepository>(
    _ => new UserRepository(connectionString));

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();


app.Run();
