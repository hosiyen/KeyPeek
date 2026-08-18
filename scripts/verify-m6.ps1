# M6 verification: the downloaded library layer. Publishes a "corrected" Chrome manifest
# on a local HTTP server, points a running install at it, and confirms the correction
# reaches the cache — and that a user override survives the update.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\verify-m6.ps1

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
dotnet build "$root\KeyPeek.sln" -c Debug -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed (is KeyPeek.exe still running?)" }

$app = "$root\src\KeyPeek\bin\Debug\net8.0-windows\KeyPeek.exe"
$log = "$env:LOCALAPPDATA\KeyPeek\logs\keypeek.log"
$settingsPath = "$env:APPDATA\KeyPeek\settings.json"
$downloadDir = "$env:LOCALAPPDATA\KeyPeek\downloaded"
$userDir = "$env:APPDATA\KeyPeek\library"
$port = Get-Random -Minimum 20000 -Maximum 45000

function LogMark { if (Test-Path $log) { @(Get-Content $log).Count } else { 0 } }
function LogTail([int]$mark) {
    $lines = @(Get-Content $log)
    if ($lines.Count -le $mark) { return "" }
    return ($lines[$mark..($lines.Count - 1)] -join "`n")
}

# 1. The "published correction": a Chrome manifest with a telltale description.
$serveDir = Join-Path $env:TEMP "keypeek-libserver"
New-Item -ItemType Directory -Force $serveDir | Out-Null
@'
PackageName: Google.Chrome
Name: Google Chrome
WindowFilter: "chrome.exe"
BackgroundProcess: false
VerifiedAgainst: "141"
Shortcuts:
  - SectionName: Tabs
    Properties:
      - Name: CORRECTED via community library
        Recommended: true
        Shortcut:
        - Win: false
          Ctrl: true
          Shift: false
          Alt: false
          Keys:
            - T
'@ | Set-Content "$serveDir\Google.Chrome.en-US.yml" -Encoding UTF8
'{ "files": ["Google.Chrome.en-US.yml"] }' | Set-Content "$serveDir\index.json" -Encoding UTF8

# 2. A user override that must SURVIVE the update (same chord, user text).
@'
PackageName: Google.Chrome
Name: Google Chrome
WindowFilter: "chrome.exe"
Shortcuts:
  - SectionName: Tabs
    Properties:
      - Name: MY OWN new-tab note
        Shortcut:
        - Win: false
          Ctrl: true
          Shift: false
          Alt: false
          Keys:
            - T
'@ | Set-Content "$userDir\my-chrome.yml" -Encoding UTF8

# 3. Local server (background job).
$server = Start-Job -ArgumentList $serveDir, $port -ScriptBlock {
    param($dir, $port)
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add("http://localhost:$port/")
    $listener.Start()
    while ($true) {
        $ctx = $listener.GetContext()
        $name = [System.IO.Path]::GetFileName($ctx.Request.Url.LocalPath)
        $file = Join-Path $dir $name
        if (Test-Path $file) {
            $bytes = [System.IO.File]::ReadAllBytes($file)
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $ctx.Response.StatusCode = 404
        }
        $ctx.Response.Close()
    }
}

try {
    # 4. Point the install at the local server, clear the stamp so it checks right away.
    Start-Process $app -ArgumentList "--quit" -Wait; Start-Sleep -Milliseconds 1000
    $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if (-not $json.PSObject.Properties['LibraryUpdate']) {
        $json | Add-Member -NotePropertyName LibraryUpdate -NotePropertyValue ([PSCustomObject]@{
            Enabled = $true; IntervalDays = 7; IndexUrl = "" })
    }
    $json.LibraryUpdate.IndexUrl = "http://localhost:$port/index.json"
    $json.LibraryUpdate.Enabled = $true
    $json | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
    Remove-Item "$downloadDir\.last-check" -Force -ErrorAction SilentlyContinue
    Remove-Item "$downloadDir\*.yml" -Force -ErrorAction SilentlyContinue

    $m = LogMark
    Start-Process $app
    # The downloader's first look is ~30 s after startup.
    $deadline = (Get-Date).AddSeconds(75); $updated = $false
    while ((Get-Date) -lt $deadline -and -not $updated) {
        Start-Sleep -Seconds 5
        $updated = (LogTail $m) -match "Community library updated: 1 definitions"
    }
    Assert "T1 published correction reaches the running install" $updated
    Assert "T2 corrected manifest is in the downloaded cache" (
        (Test-Path "$downloadDir\Google.Chrome.en-US.yml") -and
        ((Get-Content "$downloadDir\Google.Chrome.en-US.yml" -Raw) -match "CORRECTED via community library"))

    # 5. User override survives: the file is untouched and the merged reload logged no errors.
    $t3 = (Test-Path "$userDir\my-chrome.yml") -and
          ((Get-Content "$userDir\my-chrome.yml" -Raw) -match "MY OWN new-tab note")
    Assert "T3 user override file survives the library update" $t3
    Assert "T4 reload after update completed cleanly" ((LogTail $m) -match "Library: \d+ apps")
}
finally {
    Start-Process $app -ArgumentList "--quit" -Wait
    Stop-Job $server -ErrorAction SilentlyContinue; Remove-Job $server -Force -ErrorAction SilentlyContinue
    # Restore: default URL back, remove the test override and test cache.
    $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($json.PSObject.Properties['LibraryUpdate']) {
        $json.LibraryUpdate.IndexUrl = "https://raw.githubusercontent.com/keypeek-app/library/main/index.json"
        $json | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
    }
    Remove-Item "$userDir\my-chrome.yml" -Force -ErrorAction SilentlyContinue
    Remove-Item "$downloadDir\Google.Chrome.en-US.yml" -Force -ErrorAction SilentlyContinue
    Remove-Item "$downloadDir\.last-check" -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "==== M6 (library updates) verification summary ===="
$results | ForEach-Object { Write-Host $_ }
if ($results -match "FAIL") { exit 1 } else { exit 0 }
