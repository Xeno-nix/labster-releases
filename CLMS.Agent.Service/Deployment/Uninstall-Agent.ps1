#Requires -RunAsAdministrator
param([switch]$PurgeCredentials)
$ErrorActionPreference = 'Stop'
$installRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'CLMS'))
$serviceExecutable = Join-Path $installRoot 'Service/CLMS.Agent.Service.exe'
$service = Get-Service -Name 'CLMS.Agent' -ErrorAction SilentlyContinue
if ($service) { Stop-Service -Name 'CLMS.Agent'; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
Remove-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CLMS.Agent.Companion' -ErrorAction SilentlyContinue
if ($PurgeCredentials -and (Test-Path -LiteralPath $serviceExecutable)) {
    $resetProcess = Start-Process -FilePath $serviceExecutable -ArgumentList '--reset' -WindowStyle Hidden -Wait -PassThru
    if ($resetProcess.ExitCode -ne 0) { throw 'Credential reset failed.' }
}
if ($service) { & sc.exe delete 'CLMS.Agent'; if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' } }
# Deliberately retain installed files and historical checkpoints; no recursive system-directory removal.
Write-Host 'Service uninstalled. Revoke the computer unit in the server registry if it is being retired. Sign out to stop the current login companion.'
