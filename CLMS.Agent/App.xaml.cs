using Microsoft.Extensions.DependencyInjection;

namespace CLMS.Agent;

public partial class App : Microsoft.Maui.Controls.Application
{
	/// <summary>Loads shared MAUI application resources.</summary>
	public App()
	{
		InitializeComponent();
	}

	/// <summary>Creates the MAUI shell window; the machine service continues independently of this window.</summary>
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}