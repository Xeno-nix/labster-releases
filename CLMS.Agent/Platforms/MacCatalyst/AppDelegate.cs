using Foundation;
using UIKit;

namespace CLMS.Agent;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);
        // The settings scene stays alive while the agent runs; Hide and Quit remain available.
        builder.RemoveMenu(UIMenuIdentifier.Close.GetConstant()!);
    }
}
