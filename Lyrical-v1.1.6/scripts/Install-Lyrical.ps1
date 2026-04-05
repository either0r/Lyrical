[CmdletBinding()]
param(
    [string]$CertPath = (Join-Path $PSScriptRoot "Lyrical_CodeSign.cer"),
    [string]$MsixPath,
    [switch]$SkipCertificateInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-Elevated {
    $argList = @(
        '-NoProfile'
        '-ExecutionPolicy', 'Bypass'
        '-File', ('"{0}"' -f $PSCommandPath)
    )

    if ($CertPath) { $argList += @('-CertPath', ('"{0}"' -f $CertPath)) }
    if ($MsixPath) { $argList += @('-MsixPath', ('"{0}"' -f $MsixPath)) }
    if ($SkipCertificateInstall) { $argList += '-SkipCertificateInstall' }

    Start-Process -FilePath 'pwsh.exe' -Verb RunAs -ArgumentList $argList | Out-Null
}

function Resolve-PackagePath {
    param([string]$CandidatePath)

    if ($CandidatePath) {
        if (-not (Test-Path -LiteralPath $CandidatePath)) {
            throw "Package file not found: $CandidatePath"
        }

        return (Resolve-Path -LiteralPath $CandidatePath).Path
    }

    $candidate = Get-ChildItem -Path $PSScriptRoot -File |
        Where-Object { $_.Extension -in '.msix', '.msixbundle' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $candidate) {
        throw "No .msix or .msixbundle found in $PSScriptRoot. Provide -MsixPath explicitly."
    }

    return $candidate.FullName
}

function Install-CertificateIfNeeded {
    param([string]$Path)

    if ($SkipCertificateInstall) {
        Write-Host 'Skipping certificate install by request.' -ForegroundColor Yellow
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Certificate file not found: $Path"
    }

    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path)
    $thumbprint = $cert.Thumbprint

    $existing = Get-ChildItem -Path Cert:\LocalMachine\Root |
        Where-Object { $_.Thumbprint -eq $thumbprint } |
        Select-Object -First 1

    if ($existing) {
        Write-Host "Certificate already trusted (thumbprint: $thumbprint)."
        return
    }

    Write-Host "Installing certificate to Trusted Root (thumbprint: $thumbprint)..."
    Import-Certificate -FilePath $Path -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Write-Host 'Certificate installed.' -ForegroundColor Green
}

if (-not (Test-IsAdministrator)) {
    Write-Host 'Requesting administrator rights...'
    Restart-Elevated
    exit 0
}

try {
    $resolvedMsixPath = Resolve-PackagePath -CandidatePath $MsixPath
    Install-CertificateIfNeeded -Path $CertPath

    Write-Host "Installing package: $resolvedMsixPath"
    Add-AppxPackage -Path $resolvedMsixPath -ForceUpdateFromAnyVersion

    Write-Host 'Lyrical installation completed successfully.' -ForegroundColor Green
}
catch {
    Write-Error $_
    exit 1
}
