# Removes a per-user KeyPeek install: app folder, Start-Menu shortcut, and the
# run-at-startup entry. Your settings and library in %APPDATA%\KeyPeek are KEPT
# unless you pass -RemoveData.
#
#   powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1 [-RemoveData]

param([switch]$RemoveData)

$ErrorActionPreference = "Stop"
$targetDir = "$env:LOCALAPPDATA\Programs\KeyPeek"
$targetExe = Join-Path $targetDir "KeyPeek.exe"

if (Test-Path $targetExe) {
    & $targetExe --quit 2>$null
    Start-Sleep -Seconds 2
}
Get-Process KeyPeek -ErrorAction SilentlyContinue | Wait-Process -Timeout 8 -ErrorAction SilentlyContinue

# The documented current-user Run key (the only registry entry KeyPeek ever writes).
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "KeyPeek" -ErrorAction SilentlyContinue

Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\KeyPeek.lnk" -ErrorAction SilentlyContinue
if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }

if ($RemoveData) {
    Remove-Item "$env:APPDATA\KeyPeek" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$env:LOCALAPPDATA\KeyPeek" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "KeyPeek and all its data removed."
} else {
    Write-Host "KeyPeek removed. Settings/library kept in $env:APPDATA\KeyPeek (use -RemoveData to delete)."
}
