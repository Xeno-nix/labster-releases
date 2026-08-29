using CLMS.Web.Components;
using CLMS.Infrastructure;
using CLMS.Application;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register infrastructure and application services
builder.Services.AddInfrastructureServices();
builder.Services.AddApplication();

// TEMP-DEBUG: browser-console logging for the terminal while there's no real DB to
// inspect. Remove this line as step 2 of the revert checklist in
// CLMS.Web/Debug/TerminalDebugLogger.cs.
builder.Services.AddScoped<CLMS.Web.Debug.TerminalDebugLogger>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
