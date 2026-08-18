# M3 verification: overlay shows/hides cleanly, never steals focus, Esc dismisses.
# Also drops screenshots in the scratch folder for visual review.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\verify-m3.ps1 [-ShotDir <dir>]

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

Write-Host "Building..."
dotnet build "$root\KeyPeek.sln" -c Debug -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }

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
function EnsureFocus {
    for ($i = 0; $i -lt 3; $i++) {
        $r = Drive "focus notepad"
        if ("$r" -match "focused") { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

Start-Process $app -ArgumentList "--quit" -Wait
Start-Sleep -Milliseconds 800
Start-Process $app
Start-Sleep -Milliseconds 2000

$notepad = Start-Process notepad -PassThru
Start-Sleep -Milliseconds 1200
Assert "notepad can be focused" (EnsureFocus)

# T1: overlay appears on hold; foreground stays notepad WHILE it is visible
$passed = $false
for ($i = 0; $i -lt 3 -and -not $passed; $i++) {
    if (-not (EnsureFocus)) { continue }
    $m = LogMark
    $out = Drive "down ctrl; sleep 900; foreground; shot $ShotDir\m3-visible.png; up ctrl; sleep 500"
    $t = (LogTail $m) -join "`n"
    $passed = ($t -match "Overlay shown \(notepad\)") -and ("$out" -match "notepad") -and ($t -match "Overlay hidden \(TriggerReleased\)")
}
Assert "T1 overlay shows on hold, focus stays on notepad, hides on release" $passed
Drive "shot $ShotDir\m3-hidden.png" | Out-Null

# T2: Esc dismisses while trigger still held
$passed = $false
for ($i = 0; $i -lt 3 -and -not $passed; $i++) {
    if (-not (EnsureFocus)) { continue }
    $m = LogMark
    Drive "down ctrl; sleep 900; press esc; sleep 400; up ctrl; sleep 300" | Out-Null
    $t = (LogTail $m) -join "`n"
    $passed = ($t -match "Overlay shown \(notepad\)") -and ($t -match "Overlay hidden \(EscPressed\)")
}
Assert "T2 Esc dismisses the overlay" $passed

# T3: focus is still with notepad after everything
Assert "T3 notepad still focused at the end" ((Drive "foreground") -match "notepad")

Start-Process $app -ArgumentList "--quit" -Wait
if (-not $notepad.HasExited) { $notepad.CloseMainWindow() | Out-Null }

Write-Host ""
Write-Host "==== M3 verification summary ===="
$results | ForEach-Object { Write-Host $_ }
Write-Host "screenshots: $ShotDir"
if ($results -match "FAIL") { exit 1 } else { exit 0 }
