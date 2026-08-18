# M4 verification: held modifiers narrow and widen the visible list live.
# Screenshots land in the scratch folder for visual review of each stage.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\verify-m4.ps1 [-ShotDir <dir>]

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

# Full narrative: hold Ctrl (all shortcuts) → +Shift (narrow) → release Shift (widen)
# → +Shift+Alt (narrow twice) → release all.
$passed = $false
for ($i = 0; $i -lt 3 -and -not $passed; $i++) {
    $m = LogMark
    # waitidle: don't inject while a person is actively using the machine
    $out = Drive ("waitidle 15 600; focus notepad; " +
           "down ctrl; sleep 900; shot $ShotDir\m4-all.png; " +
           "down shift; sleep 450; shot $ShotDir\m4-ctrl-shift.png; " +
           "up shift; sleep 450; shot $ShotDir\m4-widened.png; " +
           "down shift; sleep 200; down alt; sleep 450; shot $ShotDir\m4-ctrl-shift-alt.png; " +
           "up alt; up shift; up ctrl; sleep 400")
    if (-not ("$out" -match "focused")) { Start-Sleep -Milliseconds 700; continue }
    $t = (LogTail $m) -join "`n"
    $passed = ($t -match "Overlay shown \(notepad\)") -and
              ($t -match [regex]::Escape("Overlay filter → Ctrl+Shift")) -and
              ($t -match [regex]::Escape("Overlay filter → Ctrl") ) -and
              ($t -match [regex]::Escape("Overlay filter → Ctrl+Alt+Shift")) -and
              ($t -match "Overlay hidden \(TriggerReleased\)")
}
Assert "T1 narrow on Shift, widen on release, narrow again with Alt" $passed

Start-Process $app -ArgumentList "--quit" -Wait
if (-not $notepad.HasExited) { $notepad.CloseMainWindow() | Out-Null }

Write-Host ""
Write-Host "==== M4 verification summary ===="
$results | ForEach-Object { Write-Host $_ }
Write-Host "screenshots: $ShotDir"
if ($results -match "FAIL") { exit 1 } else { exit 0 }
