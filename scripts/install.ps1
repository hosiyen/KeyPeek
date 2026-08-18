# Installs KeyPeek per-user (no admin): copies the self-contained exe to
# %LOCALAPPDATA%\Programs\KeyPeek, adds a Start-Menu shortcut, and starts it.
# Settings/library data live in %APPDATA%\KeyPeek and are untouched.
#
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1            # use dist\KeyPeek.exe
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Rebuild   # publish first
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -NoStart

param([switch]$Rebuild, [switch]$NoStart)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# Find the exe: next to this script (the "copied two files to a USB stick" case),
# then the repo's dist\, then build it if we're in a repo.
$candidates = @(
    (Join-Path $PSScriptRoot "KeyPeek.exe"),
    (Join-Path $root "dist\KeyPeek.exe")
)
$dist = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($Rebuild -or -not $dist) {
    $publish = Join-Path $PSScriptRoot "publish.ps1"
    if (-not (Test-Path $publish)) {
        throw "KeyPeek.exe not found next to install.ps1 (or in ..\dist), and no publish.ps1 to build it. Put KeyPeek.exe in the same folder as this script."
    }
    & $publish
    $dist = Join-Path $root "dist\KeyPeek.exe"
}

$targetDir = "$env:LOCALAPPDATA\Programs\KeyPeek"
$targetExe = Join-Path $targetDir "KeyPeek.exe"

# Ask any running instance (from any location) to exit cleanly before overwriting.
& $dist --quit 2>$null
Start-Sleep -Seconds 2
Get-Process KeyPeek -ErrorAction SilentlyContinue | Wait-Process -Timeout 8 -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $targetDir | Out-Null
Copy-Item $dist $targetExe -Force

# Per-user Start-Menu shortcut (searchable from the Start menu).
$shortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\KeyPeek.lnk"
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $targetExe
$lnk.WorkingDirectory = $targetDir
$lnk.Description = "Hold Ctrl/Win/Alt to see the focused app's keyboard shortcuts"
$lnk.Save()

if (-not $NoStart) {
    Start-Process $targetExe
}

Write-Host ""
Write-Host "KeyPeek installed:"
Write-Host "  App        $targetExe"
Write-Host "  Start Menu $shortcut"
Write-Host "  Data       $env:APPDATA\KeyPeek  (settings + your library)"
Write-Host ""
Write-Host "Autostart: tray icon -> Open KeyPeek -> 'Start KeyPeek when I sign in'."
# Name the uninstaller that actually exists next to this script: in the repo that is
# scripts\uninstall.ps1, in the distributed zip it is Uninstall.cmd beside the exe.
$uninstaller = if (Test-Path "$PSScriptRoot\Uninstall.cmd") { "Uninstall.cmd (in this folder)" }
               else { "scripts\uninstall.ps1" }
Write-Host "Uninstall: $uninstaller"
