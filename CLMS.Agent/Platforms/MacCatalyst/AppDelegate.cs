using Foundation;
namespace CLMS.Agent;
[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    /// <summary>Connects the Mac Catalyst application delegate to the shared MAUI builder.</summary>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
