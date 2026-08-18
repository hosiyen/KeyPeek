# M1 verification: hold detection + focused-app resolution, with no typing interference.
# Injects input with SendInput via the KeyPeekDriver test harness and asserts on the log.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File scripts\verify-m1.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\_dotnet.ps1"
$dotnet = Resolve-Dotnet

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
& $dotnet build "$root\src\KeyPeek\KeyPeek.csproj" -c Debug -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "app build failed" }
& $dotnet build "$root\tools\KeyPeekDriver\KeyPeekDriver.csproj" -c Debug -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "driver build failed" }

$app = "$root\src\KeyPeek\bin\Debug\net8.0-windows\KeyPeek.exe"
$driver = "$root\tools\KeyPeekDriver\bin\Debug\net8.0-windows\KeyPeekDriver.exe"
$log = "$env:LOCALAPPDATA\KeyPeek\logs\keypeek.log"

# Every injection waits for the machine to go quiet first. Without this the results are
# meaningless on a live desktop: a human's own Ctrl press lands inside a test's log window
# and reads as a spurious detection, while their click cancels the hold we just started.
$IdleSeconds = 3
$IdleTimeout = 180
function Drive([string]$script) { & $driver "waitidle $IdleSeconds $IdleTimeout; $script" }
function DriveIdle([string]$script) {
    # Returns $null when the machine never went quiet, so callers retry instead of asserting
    # on data gathered while someone was typing.
    $out = "$(Drive $script)"
    if ($out -match "not-idle-timeout") { return $null }
    return $out
}
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

# Fresh start
Start-Process $app -ArgumentList "--quit" -Wait
Start-Sleep -Milliseconds 800
Start-Process $app
Start-Sleep -Milliseconds 2000
$m = LogMark
Assert "app started and hooks installed" ((LogTail 0) -join "`n" -match "hooks installed")

# A scratch notepad is the foreground app for all tests (fresh instance, no unsaved content)
$notepad = Start-Process notepad -PassThru
Start-Sleep -Milliseconds 1200
Assert "notepad can be focused" (EnsureFocus)

# Positive detections retry a few times: a live desktop can steal focus or click
# mid-test, cancelling the hold (which is correct behavior, but not what we're testing).
function DetectWithRetry([string]$driveScript) {
    for ($i = 0; $i -lt 3; $i++) {
        if (-not (EnsureFocus)) { continue }
        $m = LogMark
        if ($null -eq (DriveIdle $driveScript)) { continue } # machine busy: not a result
        Start-Sleep -Milliseconds 450
        $t = (LogTail $m) -join "`n"
        if ($t -match "Hold detected.*notepad") { return $true }
    }
    return $false
}

# Negative tests need the same gate: "no detection" is only meaningful if nobody else was
# pressing keys. Retries here guard against a human's Ctrl landing in our window.
function SilentWithRetry([string]$driveScript, [int]$settleMs) {
    for ($i = 0; $i -lt 3; $i++) {
        if (-not (EnsureFocus)) { continue }
        $m = LogMark
        if ($null -eq (DriveIdle $driveScript)) { continue }
        if ($settleMs -gt 0) { Start-Sleep -Milliseconds $settleMs }
        $t = (LogTail $m) -join "`n"
        if (-not ($t -match "Hold detected")) { return $true }
    }
    return $false
}

# T1: clean 700 ms hold → detection with correct app name
Assert "T1 hold Ctrl 700ms detects notepad" (DetectWithRetry "down ctrl; sleep 700; up ctrl")

# T2: Ctrl+key chord → NO detection, even though Ctrl stays held past the delay
Assert "T2 Ctrl+key chord stays silent" `
    (SilentWithRetry "down ctrl; sleep 80; press f13; sleep 700; up ctrl" 400)

# T3: quick tap below the threshold → NO detection
Assert "T3 short Ctrl tap stays silent" `
    (SilentWithRetry "down ctrl; sleep 120; up ctrl; sleep 600" 0)

# T4: a second modifier during the hold window must NOT cancel (progressive filter flow)
Assert "T4 Ctrl then Shift still detects" (DetectWithRetry "down ctrl; sleep 100; down shift; sleep 600; up shift; up ctrl")

# T5: clean shutdown uninstalls hooks
$m = LogMark
Start-Process $app -ArgumentList "--quit" -Wait
Start-Sleep -Milliseconds 1500
$t5 = (LogTail $m) -join "`n"
Assert "T5 clean exit uninstalls hooks" (($t5 -match "hooks uninstalled") -and ($t5 -match "exited cleanly"))

# Cleanup the scratch notepad (fresh instance — nothing to save)
if (-not $notepad.HasExited) { $notepad.CloseMainWindow() | Out-Null }

Write-Host ""
Write-Host "==== M1 verification summary ===="
$results | ForEach-Object { Write-Host $_ }
if ($results -match "FAIL") { exit 1 } else { exit 0 }
