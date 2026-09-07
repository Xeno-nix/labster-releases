using CLMS.Web.Components;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Services;
using CLMS.Infrastructure.Repositories;
using CLMS.Infrastructure.Services;
using CLMS.Infrastructure;
using CLMS.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using CLMS.Application;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddInfrastructure()
    .AddRazorComponents()
        .AddInteractiveServerComponents();

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(option =>
    {
        option.LoginPath = "/login";
        option.LogoutPath = "/logout";
        option.ExpireTimeSpan = TimeSpan.FromHours(8);
        option.SlidingExpiration = true;
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

app.MapStaticAssets();

// Admin logbook CSV export — a plain HTTP GET endpoint (not a Blazor interactive
// action) so the page can link to it with a normal <a href>, exactly like Laravel's
// real link + streamDownload(). Query params mirror LogbookQueryDTO.
app.MapGet("/logbook/export", async (
    string? search,
    string? filter,
    CLMS.Application.Interfaces.ILogbookService logbookService) =>
{
    var query = new CLMS.Application.DTOs.Logbook.LogbookQueryDTO
    {
        Search = search,
        Filter = filter,
    };

    var csv = await logbookService.ExportCsvAsync(query);
    var fileName = $"logbook-{DateTime.Now:yyyy-MM-dd-HHmmss}.csv";
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

    return Results.File(bytes, "text/csv", fileName);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
