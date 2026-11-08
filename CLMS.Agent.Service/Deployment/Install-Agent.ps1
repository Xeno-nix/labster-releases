#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory)][string]$ServicePublishPath,
    [Parameter(Mandatory)][string]$UiPublishPath
)
$ErrorActionPreference = 'Stop'
$serviceSource = (Resolve-Path -LiteralPath $ServicePublishPath).Path
$uiSource = (Resolve-Path -LiteralPath $UiPublishPath).Path
if (!(Test-Path -LiteralPath (Join-Path $serviceSource 'CLMS.Agent.Service.exe')) -or
    !(Test-Path -LiteralPath (Join-Path $uiSource 'CLMS.Agent.exe'))) { throw 'Supply published service and unpackaged MAUI UI directories.' }
$installRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'CLMS'))
$serviceTarget = Join-Path $installRoot 'Service'
$uiTarget = Join-Path $installRoot 'UI'
$stateTarget = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'CLMS/Agent'))
if ($serviceSource.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $uiSource.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish outside the installation directory before upgrading.' }
$existing = Get-Service -Name 'CLMS.Agent' -ErrorAction SilentlyContinue
if ($existing) { Stop-Service -Name 'CLMS.Agent'; $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
New-Item -ItemType Directory -Path $serviceTarget, $uiTarget, $stateTarget -Force | Out-Null
# Machine credentials remain outside the binary directories and survive upgrades.
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
    $identity = New-Object Security.Principal.SecurityIdentifier($sid)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
}
Set-Acl -LiteralPath $stateTarget -AclObject $acl
Get-ChildItem -LiteralPath $serviceSource -Force | Copy-Item -Destination $serviceTarget -Recurse -Force
Get-ChildItem -LiteralPath $uiSource -Force | Copy-Item -Destination $uiTarget -Recurse -Force
$serviceExecutable = Join-Path $serviceTarget 'CLMS.Agent.Service.exe'
if (!$existing) { New-Service -Name 'CLMS.Agent' -DisplayName 'CLMS Computer Agent' -BinaryPathName ('"' + $serviceExecutable + '"') -StartupType Automatic | Out-Null }
& sc.exe config 'CLMS.Agent' obj= LocalSystem start= auto | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not configure the machine service account.' }
& sc.exe failure 'CLMS.Agent' reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not configure service restart policy.' }
# This command runs as the interactive user. The service itself never opens a desktop window.
$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $runKey -Name 'CLMS.Agent.Companion' -PropertyType String -Value ('"' + $serviceExecutable + '" --watch-ui') -Force | Out-Null
Start-Service -Name 'CLMS.Agent'
Write-Host 'Installed CLMS service. Open the CLMS UI to configure HTTPS and register. The login companion starts at the next sign-in.'
