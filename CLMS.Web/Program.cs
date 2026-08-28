using CLMS.Web.Components;
using CLMS.Application.Interfaces;
using CLMS.Application.Interfaces.Repositories;
using CLMS.Application.Services;
using CLMS.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();


// Database connection
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");


// Application Services
builder.Services.AddScoped<IClassScheduleService, ClassScheduleService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ILaboratoryService, LaboratoryService>();
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<IAcademicTermService, AcademicTermService>();
builder.Services.AddScoped<IUserService, UserService>();
// Repositories
builder.Services.AddScoped<IClassScheduleRepository>(
    _ => new ClassScheduleRepository(connectionString!));
builder.Services.AddScoped<IBookingRepository>(
    _ => new BookingRepository(connectionString!));
builder.Services.AddScoped<ILaboratoryRepository>(
    _ => new LaboratoryRepository(connectionString!));
builder.Services.AddScoped<ICourseRepository>(
    _ => new CourseRepository(connectionString!));
builder.Services.AddScoped<IAcademicTermRepository>(
    _ => new AcademicTermRepository(connectionString!));
    builder.Services.AddScoped<IUserRepository>(
    _ => new UserRepository(connectionString!));

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

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();