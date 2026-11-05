
## Run

Install the .NET 10 SDK and MAUI Windows workload on your development machine:

```powershell
dotnet workload install maui-windows
```

From the solution root, build and launch:

```powershell
dotnet build CLMS.Agent/CLMS.Agent.csproj -t:Run
```

## Where code belongs

| Folder | Purpose |
| --- | --- |
| `Views/` | XAML screens and their code-behind; includes `MainPage`. |
| `ViewModels/` | Screen state and commands. |
| `Models/` | Desktop-only data models; shared contracts belong in `CLMS.Shared`. |
| `Services/` | Future API clients, server connections, and screen capture. |
| `Configuration/` | App options such as the server address; never commit secrets. |
| `Platforms/Windows/` | Windows startup files and manifests. |
| `Properties/` | Development launch profiles in `launchSettings.json`. |
| `Resources/` | Icons, splash artwork, images, fonts, styles, and raw assets. |

Empty folders use `.gitkeep` so Git preserves them. Remove the placeholder after adding real files.

## Main files

- `CLMS.Agent.csproj`: Windows target, dependencies, shared project reference, and resource settings.
- `MauiProgram.cs`: app configuration and service registration.
- `App.xaml` / `App.xaml.cs`: global resources and application window creation.
- `AppShell.xaml` / `AppShell.xaml.cs`: navigation and the initial page.
- `Resources/Styles/Colors.xaml` / `Styles.xaml`: shared colors and control styles.

Startup flow: Windows entry point → `MauiProgram` → `App` → `AppShell` → `Views/MainPage`.