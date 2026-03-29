# Lyrical Installer Scripts

These scripts help install and uninstall `Lyrical` for sideloaded MSIX distribution.

## Files to include in your release folder

- `Lyrical.msix` (or `.msixbundle`)
- `Lyrical_CodeSign.cer`
- `Install-Lyrical.ps1`
- `Uninstall-Lyrical.ps1`

## Prerequisites

- Windows machine with PowerShell 7 (`pwsh`) or Windows PowerShell
- Right-click PowerShell and run as Administrator (required for certificate setup)

## Install

From the folder containing all release files:

```powershell
pwsh -ExecutionPolicy Bypass -File .\Install-Lyrical.ps1 -CertPath .\Lyrical_CodeSign.cer -MsixPath .\Lyrical.msix
```

### Optional

If the certificate is already trusted on the machine:

```powershell
pwsh -ExecutionPolicy Bypass -File .\Install-Lyrical.ps1 -SkipCertificateInstall -MsixPath .\Lyrical.msix
```

If `-MsixPath` is omitted, the installer script picks the newest `.msix`/`.msixbundle` in the same folder.

## Uninstall app

```powershell
pwsh -ExecutionPolicy Bypass -File .\Uninstall-Lyrical.ps1
```

## Uninstall app and remove trusted certificate

```powershell
pwsh -ExecutionPolicy Bypass -File .\Uninstall-Lyrical.ps1 -RemoveCertificate -CertPath .\Lyrical_CodeSign.cer
```

## Troubleshooting

- `Access is denied` or certificate import fails:
  - Re-run the script in an elevated PowerShell (Run as Administrator).
- `No .msix or .msixbundle found`:
  - Pass `-MsixPath` explicitly.
- `Add-AppxPackage` errors:
  - Confirm the package matches your Windows architecture and isn't blocked by SmartScreen/policy.
