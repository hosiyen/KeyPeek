# M5 verification: redesigned overlay, multi-trigger (Ctrl/Win/Alt), Win-release masking.
# Idle-gated: injections wait until nobody is actively using the machine.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\verify-m5.ps1 [-ShotDir <dir>]

param([string]$ShotDir = "$env:TEMP\keypeek-shots")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\_dotnet.ps1"
$dotnet = Resolve-Dotnet
New-Item -ItemType Directory -Force $ShotDir | Out-Null

$results = New-Object System.Collections.ArrayList
function Assert([string]$name, [bool]$condition) {
    $status = if ($condition) { "PASS" } else { "FAIL" }
    [void]$results.Add("[$status] $name")
    Write-Host "[$status] $name"
}

# A previous stage may have left an instance running; builds fail on locked binaries.
$prebuilt = "$root\src\KeyPeek\bin\Debug\net8.0-windows\KeyPeek.exe"
if (Test-Path $prebuilt) { Start-Process $prebuilt -ArgumentList "--quit" -Wait; Start-Sleep -Milliseconds 1500 }

Write-Host "Building..."
dotnet build "$root\KeyPeek.sln" -c Debug -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed (is KeyPeek.exe still running?)" }

$app = "$root\src\KeyPeek\bin\Debug\net8.0-windows\KeyPeek.exe"
$driver = "$root\tools\KeyPeekDriver\bin\Debug\net8.0-windows\KeyPeekDriver.exe"
$log = "$env:LOCALAPPDATA\KeyPeek\logs\keypeek.log"

function Drive([string]$script) { & $driver $script }
function LogTail([int]$mark) {
    if (-not (Test-Path $log)) { return @() }
    $lines = @(Get-Content $log)
    if ($lines.Count -le $mark) { return @() }
    return $lines[$mark..($lines.Count - 1)]
}
function LogMark { if (Test-Path $log) { @(Get-Content $log).Count } else { 0 } }

Start-Process $app -ArgumentList "--quit" -Wait
Start-Sleep -Milliseconds 800
Start-Process $app
Start-Sleep -Milliseconds 2000

# The scratch notepad can be closed by a human mid-run; relaunch it as needed.
function EnsureNotepad {
    if (-not (Get-Process notepad -ErrorAction SilentlyContinue |
              Where-Object { $_.MainWindowHandle -ne 0 })) {
        Start-Process notepad | Out-Null
        Start-Sleep -Milliseconds 1200
    }
}
EnsureNotepad

# T1: Ctrl hold → redesigned overlay, focus stays put
$passed = $false
for ($i = 0; $i -lt 3 -and -not $passed; $i++) {
    EnsureNotepad
    $m = LogMark
    $out = Drive ("waitidle 12 600; focus notepad; down ctrl; sleep 1300; foreground; " +
                  "shot $ShotDir\m5-ctrl.png; up ctrl; sleep 500")
    if (-not ("$out" -match "focused")) { Start-Sleep -Milliseconds 700; continue }
    $t = (LogTail $m) -join "`n"
    $passed = ($t -match "Overlay shown \(notepad\)") -and ("$out" -match "notepad") -and
              ($t -match "Overlay hidden \(TriggerReleased\)")
}
Assert "T1 Ctrl hold shows overlay, focus stays, hides on release" $passed

# T2: Win hold → overlay (filtered to Win); releasing Win must NOT open the Start menu
$passed = $false
for ($i = 0; $i -lt 3 -and -not $passed; $i++) {
    EnsureNotepad
    $m = LogMark
    $out = Drive ("waitidle 12 600; focus notepad; down win; sleep 1300; " +
                  "shot $ShotDir\m5-win.png; up win; sleep 700; foreground")
    if (-not ("$out" -match "focused")) { Start-Sleep -Milliseconds 700; continue }
    $t = (LogTail $m) -join "`n"
    # If the Start menu had opened, the last `foreground` line would be
    # StartMenuExperienceHost/SearchApp instead of notepad.
    $fg = @($out) | Select-Object -Last 1
    $passed = ($t -match "Overlay shown \(notepad\)") -and ($fg -eq "notepad")
}
Assert "T2 Win hold shows overlay and release does not open Start" $passed

# T3: Alt hold works too
$passed = $false
for ($i = 0; $i -lt 3 -and -not $passed; $i++) {
    EnsureNotepad
    $m = LogMark
    $out = Drive ("waitidle 12 600; focus notepad; down alt; sleep 1300; shot $ShotDir\m5-alt.png; up alt; sleep 500; foreground")
    if (-not ("$out" -match "focused")) { Start-Sleep -Milliseconds 700; continue }
    $t = (LogTail $m) -join "`n"
    $fg = @($out) | Select-Object -Last 1
    $passed = ($t -match "Overlay shown \(notepad\)") -and ($fg -eq "notepad")
}
Assert "T3 Alt hold shows overlay, release harmless" $passed

Start-Process $app -ArgumentList "--quit" -Wait
Get-Process notepad -ErrorAction SilentlyContinue |
    ForEach-Object { $_.CloseMainWindow() | Out-Null }

Write-Host ""
Write-Host "==== M5 verification summary ===="
$results | ForEach-Object { Write-Host $_ }
Write-Host "screenshots: $ShotDir"
if ($results -match "FAIL") { exit 1 } else { exit 0 }
