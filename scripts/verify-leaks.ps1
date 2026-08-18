# Quality-bar check: no orphaned process/hook after 50 launch+quit cycles, and stable
# handle count / working set on a live instance across repeated overlay cycles.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\verify-leaks.ps1

param([int]$Cycles = 50, [int]$OverlayCycles = 20)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\_dotnet.ps1"
$dotnet = Resolve-Dotnet
$app = "$root\src\KeyPeek\bin\Debug\net8.0-windows\KeyPeek.exe"
$driver = "$root\tools\KeyPeekDriver\bin\Debug\net8.0-windows\KeyPeekDriver.exe"
$log = "$env:LOCALAPPDATA\KeyPeek\logs\keypeek.log"

$results = New-Object System.Collections.ArrayList
function Assert([string]$name, [bool]$condition) {
    $status = if ($condition) { "PASS" } else { "FAIL" }
    [void]$results.Add("[$status] $name")
    Write-Host "[$status] $name"
}

Start-Process $app -ArgumentList "--quit" -Wait; Start-Sleep -Milliseconds 800

# --- 1. 50x launch + clean quit: no zombie processes, hooks uninstalled every time ---
$m0 = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 }
$zombies = 0
for ($i = 1; $i -le $Cycles; $i++) {
    Start-Process $app
    # Wait for readiness (hooks installed) rather than guessing a settle time.
    $ready = (Get-Date).AddSeconds(8)
    do { Start-Sleep -Milliseconds 200 }
    until ((Get-Date) -gt $ready -or ((Test-Path $log) -and
           ((Get-Content $log -Tail 5) -match "Ready|hooks installed")))
    Start-Process $app -ArgumentList "--quit" -Wait
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and (Get-Process KeyPeek -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 150
    }
    if (Get-Process KeyPeek -ErrorAction SilentlyContinue) {
        $zombies++
        Get-Process KeyPeek | Stop-Process -Force
    }
}
$tail = (@(Get-Content $log)[$m0..(@(Get-Content $log).Count - 1)]) -join "`n"
$uninstalls = ([regex]::Matches($tail, "Global hooks uninstalled")).Count
$cleanExits = ([regex]::Matches($tail, "KeyPeek exited cleanly")).Count
Assert "50x launch+quit leaves no zombie process ($zombies zombies)" ($zombies -eq 0)
Assert "hooks uninstalled on every exit ($uninstalls/$Cycles)" ($uninstalls -ge $Cycles)
Assert "clean shutdown logged every time ($cleanExits/$Cycles)" ($cleanExits -ge $Cycles)

# --- 2. live instance: handle/memory stability across overlay cycles ---
#
# Two things this used to get wrong, both of which reported a leak where there is none:
#
#   * It measured WORKING SET. KeyPeek is a 63 MB single-file exe, so its working set is
#     mostly mapped image pages plus whatever the OS has not trimmed yet; it moved tens of
#     MB in either direction between samples on an idle machine. Private bytes is the
#     number that means "this process is holding memory".
#   * It took the baseline before the overlay had ever been shown. The first show builds
#     ~150 row elements and every render surface behind them — a real, one-time cost that
#     the old baseline charged to the cycles. Warm up first, then measure the SLOPE.
#
# What a genuine leak looks like here is a monotone climb in private bytes plus a climb in
# GDI/USER objects. .NET's own sawtooth (allocate, collect, drop back) is not that, so the
# private-bytes tolerance is loose and the object-count tolerances are tight.
Add-Type -Namespace KP -Name Gui -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint GetGuiResources(System.IntPtr hProcess, uint uiFlags);
'@

Start-Process $app
Start-Sleep -Seconds 4
$proc = Get-Process KeyPeek

$shown = 0
function Cycles([int]$n) {
    for ($i = 1; $i -le $n; $i++) {
        $out = & $driver "waitidle 5 60; down ctrl; sleep 650; up ctrl; sleep 250"
        if ("$out" -match "idle") { $script:shown++ }
    }
}
function Snapshot {
    Start-Sleep -Seconds 2
    $proc.Refresh()
    [pscustomobject]@{
        Handles = $proc.HandleCount
        Private = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        Gdi     = [KP.Gui]::GetGuiResources($proc.Handle, 0)
        User    = [KP.Gui]::GetGuiResources($proc.Handle, 1)
    }
}

Cycles 5                     # warm: first show pays for the whole visual tree
$a = Snapshot
Cycles $OverlayCycles
$b = Snapshot

Write-Host ("handles {0} -> {1}   private {2} -> {3} MB   gdi {4} -> {5}   user {6} -> {7}   (cycles: {8}+5/{9}+5)" -f `
    $a.Handles, $b.Handles, $a.Private, $b.Private, $a.Gdi, $b.Gdi, $a.User, $b.User, $shown, $OverlayCycles)
Assert "handle count stable (delta $($b.Handles - $a.Handles) <= 25)" (($b.Handles - $a.Handles) -le 25)
Assert "GDI objects stable (delta $($b.Gdi - $a.Gdi) <= 10)" (($b.Gdi - $a.Gdi) -le 10)
Assert "USER objects stable (delta $($b.User - $a.User) <= 10)" (($b.User - $a.User) -le 10)
Assert "private bytes stable (delta $([Math]::Round($b.Private - $a.Private,1)) MB <= 40)" `
    (($b.Private - $a.Private) -le 40)

# leave the instance running for the user; sweep any ghost tray icons the cycles left
& "$PSScriptRoot\clean-tray.ps1" | Out-Null

Write-Host ""
Write-Host "==== leak/memory verification summary ===="
$results | ForEach-Object { Write-Host $_ }
if ($results -match "FAIL") { exit 1 } else { exit 0 }
