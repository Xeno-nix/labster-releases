[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [System.Net.IPAddress]$ServerIp
)

$ErrorActionPreference = 'Stop'
if ($ServerIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw 'Use the Ethernet IPv4 address of the laptop running CLMS.Web.'
}

$assigned = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
    Where-Object { $_.OperationalStatus -eq 'Up' } |
    ForEach-Object { $_.GetIPProperties().UnicastAddresses } |
    Where-Object { $_.Address.Equals($ServerIp) }
if (-not $assigned) { throw "This laptop does not currently have $ServerIp. Connect Ethernet and check ipconfig again." }

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'artifacts\lan'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$address = $ServerIp.ToString()
$subject = "CLMS LAN $address"
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq "CN=$subject" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate -Type SSLServerAuthentication `
        -Subject "CN=$subject" -FriendlyName $subject `
        -TextExtension "2.5.29.17={text}DNS=localhost&IPAddress=127.0.0.1&IPAddress=$address" `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -CertStoreLocation 'Cert:\CurrentUser\My'
}

# Export only the public certificate. The private key stays in this OS user's store.
$publicCertificate = Join-Path $outputDirectory 'CLMS-LAN.cer'
Export-Certificate -Cert $certificate -FilePath $publicCertificate -Force | Out-Null
if (-not (Test-Path -LiteralPath "Cert:\CurrentUser\Root\$($certificate.Thumbprint)")) {
    Import-Certificate -FilePath $publicCertificate -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
}

@{
    ServerIp = $address
    CertificateSubject = $subject
    CertificateThumbprint = $certificate.Thumbprint
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'server.json') -Encoding UTF8

Write-Host "Server address: https://${address}:7190"
Write-Host "Public certificate to copy to the other laptop: $publicCertificate"
Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
Write-Host 'Next: enable the LAN firewall rule, then run scripts\Start-LanServer.ps1.'
