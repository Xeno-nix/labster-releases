#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\lan\server.json'
if (-not (Test-Path -LiteralPath $settingsPath)) { throw 'Run Setup-LanServer.ps1 first.' }
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$address = [System.Net.IPAddress]::Parse($settings.ServerIp).ToString()
$ruleName = 'CLMS-LAN-7190-' + $address.Replace('.', '-')

if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) {
    Enable-NetFirewallRule -Name $ruleName
} else {
    New-NetFirewallRule -Name $ruleName -DisplayName "CLMS LAN HTTPS ($address)" `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7190 `
        -LocalAddress $address -RemoteAddress LocalSubnet -Profile Any | Out-Null
}
Write-Host "Allowed local-network connections to ${address}:7190."
