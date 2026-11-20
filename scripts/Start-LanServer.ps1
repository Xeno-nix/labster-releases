[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $projectRoot 'artifacts\lan\server.json'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw 'Run scripts\Setup-LanServer.ps1 -ServerIp <your-Ethernet-IP> first.'
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$address = [System.Net.IPAddress]::Parse($settings.ServerIp).ToString()
$assignedAddress = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
    Where-Object { $_.OperationalStatus -eq 'Up' } |
    ForEach-Object { $_.GetIPProperties().UnicastAddresses } |
    Where-Object { $_.Address.ToString() -eq $address }
if (-not $assignedAddress) {
    throw "The saved LAN server IP $address is not currently assigned to a connected adapter on this laptop. Reconnect the Ethernet cable and check ipconfig. If the IPv4 address changed, rerun scripts\Setup-LanServer.ps1 -ServerIp <new-IP>, enable its firewall rule, and install the new public certificate on the monitored laptops."
}

if (-not (Test-Path -LiteralPath "Cert:\CurrentUser\My\$($settings.CertificateThumbprint)")) {
    throw 'Run the server under the same OS user who ran Setup-LanServer.ps1, or repeat setup for this user.'
}

Write-Host "Other laptops connect to https://${address}:7190"
Write-Host 'If port 7190 is already in use, stop the previous CLMS.Web terminal with Ctrl+C first.'

# Command-line settings affect this launch only; existing User Secrets are preserved.
& dotnet run --project (Join-Path $projectRoot 'CLMS.Web') --launch-profile https -- `
    --urls "https://${address}:7190;https://localhost:7190" `
    --Kestrel:Certificates:Default:Subject $settings.CertificateSubject `
    --Kestrel:Certificates:Default:Store My `
    --Kestrel:Certificates:Default:Location CurrentUser
if ($LASTEXITCODE -ne 0) {
    throw "CLMS.Web exited with code $LASTEXITCODE. The original build or startup error is in the dotnet output above this message."
}
