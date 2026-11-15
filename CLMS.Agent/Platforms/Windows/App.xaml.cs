using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CLMS.Agent.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    private readonly Mutex instanceMutex = new(false, @"Local\CLMS.Agent." + Environment.UserName);
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
        bool ownsInstance;
        try { ownsInstance = instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance)
        {
            // Reopening the installed app brings up the existing agent, rather than
            // creating a second connection using the same device credential.
            var message = CLMS.Agent.Platforms.Windows.WindowsDesktopAgentService.RegisterWindowMessage("CLMS.Agent.ShowSettings");
            CLMS.Agent.Platforms.Windows.WindowsDesktopAgentService.PostMessage((nint)0xffff, message, 0, 0);
            Environment.Exit(0);
        }
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

