using Microsoft.Extensions.Logging;

namespace CLMS.Agent;

public static class MauiProgram
{
	/// <summary>Builds the MAUI application with its shared resources and development logging.</summary>
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

#if WINDOWS
		builder.Services.AddSingleton<CLMS.Agent.Core.IDeviceKeyStore, CLMS.Agent.Service.WindowsDeviceKeyStore>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IScreenCaptureService, CLMS.Agent.Platforms.Windows.WindowsScreenCaptureService>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IDesktopAgentService, CLMS.Agent.Platforms.Windows.WindowsDesktopAgentService>();
#elif MACCATALYST
		builder.Services.AddSingleton<CLMS.Agent.Core.IDeviceKeyStore, CLMS.Agent.Service.MacDeviceKeyStore>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IScreenCaptureService, CLMS.Agent.Platforms.MacCatalyst.MacScreenCaptureService>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IDesktopAgentService, CLMS.Agent.Platforms.MacCatalyst.MacDesktopAgentService>();
#else
#error Register a screen capture implementation for this platform.
#endif
		builder.Services.AddTransient<CLMS.Agent.Views.MainPage>();
		builder.Services.AddSingleton(_ => new CLMS.Agent.Services.SettingsSession(
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(15) }));
		builder.Services.AddSingleton<CLMS.Agent.Services.AgentConnectionService>();
		builder.Services.AddSingleton<CLMS.Agent.Services.ApplicationProcessMonitor>();
		builder.Services.AddSingleton<CLMS.Agent.Services.ScreenCastSender>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IAgentConnectionTransport>(services => services.GetRequiredService<CLMS.Agent.Services.AgentConnectionService>());
		builder.Services.AddSingleton<CLMS.Agent.Services.AgentRuntimeService>();
		builder.Services.AddSingleton<CLMS.Agent.Core.IAgentVault, CLMS.Agent.Service.UserAgentVault>();
		builder.Services.AddSingleton(_ => new CLMS.Agent.Core.AgentApi(
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) }));
		builder.Services.AddSingleton<CLMS.Agent.Core.AgentCoordinator>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IAgentIdentityRuntime>(sp => sp.GetRequiredService<CLMS.Agent.Core.AgentCoordinator>());
		builder.Services.AddSingleton<CLMS.Agent.Services.IAgentAccessTokenProvider>(sp => sp.GetRequiredService<CLMS.Agent.Core.AgentCoordinator>());
		return builder.Build();
	}
}
