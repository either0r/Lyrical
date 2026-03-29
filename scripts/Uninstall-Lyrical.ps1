[CmdletBinding()]
param(
    [string]$PackageNamePattern = 'Lyrical',
    [string]$CertPath = (Join-Path $PSScriptRoot "Lyrical_CodeSign.cer"),
    [switch]$RemoveCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-LyricalPackages {
    param([string]$Pattern)

    $packages = Get-AppxPackage -Name "*$Pattern*"

    if (-not $packages) {
        Write-Host "No installed package matched pattern '$Pattern'."
        return
    }

    foreach ($pkg in $packages) {
        Write-Host "Removing package: $($pkg.Name)"
        Remove-AppxPackage -Package $pkg.PackageFullName
    }

    Write-Host 'Package removal completed.' -ForegroundColor Green
}

function Remove-TrustedCertificate {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Certificate file not found: $Path"
    }

    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path)
    $thumbprint = $cert.Thumbprint

    $match = Get-ChildItem -Path Cert:\LocalMachine\Root |
        Where-Object { $_.Thumbprint -eq $thumbprint } |
        Select-Object -First 1

    if (-not $match) {
        Write-Host 'Trusted certificate not found. Skipping certificate removal.'
        return
    }

    Write-Host "Removing trusted certificate (thumbprint: $thumbprint)..."
    Remove-Item -LiteralPath $match.PSPath
    Write-Host 'Certificate removal completed.' -ForegroundColor Green
}

try {
    Remove-LyricalPackages -Pattern $PackageNamePattern

    if ($RemoveCertificate) {
        if (-not (Test-IsAdministrator)) {
            throw 'Administrator rights are required to remove a certificate from Trusted Root. Re-run in an elevated shell.'
        }

        Remove-TrustedCertificate -Path $CertPath
    }

    Write-Host 'Lyrical uninstall script completed.' -ForegroundColor Green
}
catch {
    Write-Error $_
    exit 1
}
