using Microsoft.Extensions.Logging;

namespace CLMS.Agent;

public static class MauiProgram
{
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
		builder.Services.AddSingleton<CLMS.Agent.Services.IScreenCaptureService, CLMS.Agent.Platforms.Windows.WindowsScreenCaptureService>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IDesktopAgentService, CLMS.Agent.Platforms.Windows.WindowsDesktopAgentService>();
#elif MACCATALYST
		builder.Services.AddSingleton<CLMS.Agent.Services.IScreenCaptureService, CLMS.Agent.Platforms.MacCatalyst.MacScreenCaptureService>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IDesktopAgentService, CLMS.Agent.Platforms.MacCatalyst.MacDesktopAgentService>();
#else
#error Register a screen capture implementation for this platform.
#endif
		builder.Services.AddTransient<CLMS.Agent.Views.MainPage>();
		builder.Services.AddSingleton<CLMS.Agent.Services.AgentConnectionService>();
		builder.Services.AddSingleton<CLMS.Agent.Services.ScreenCastSender>();
		builder.Services.AddSingleton<CLMS.Agent.Services.AgentSetupStore>();
		builder.Services.AddSingleton<CLMS.Agent.Services.IAgentSetupStore>(services => services.GetRequiredService<CLMS.Agent.Services.AgentSetupStore>());
		builder.Services.AddSingleton<CLMS.Agent.Services.IAgentConnectionTransport>(services => services.GetRequiredService<CLMS.Agent.Services.AgentConnectionService>());
		builder.Services.AddSingleton<CLMS.Agent.Services.AgentRuntimeService>();
		return builder.Build();
	}
}
