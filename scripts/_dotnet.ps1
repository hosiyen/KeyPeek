# Shared SDK resolution for the verify/publish scripts.
#
# This machine has a runtime-only dotnet.exe on PATH plus the real SDK in the user-scope
# install (%LOCALAPPDATA%\Microsoft\dotnet). Testing `Get-Command dotnet` alone therefore
# picks a dotnet that cannot build ("No .NET SDKs were found"), depending on whatever PATH
# the caller happened to have. Ask each candidate whether it actually has an SDK instead.
#
# Usage:  . "$PSScriptRoot\_dotnet.ps1"; $dotnet = Resolve-Dotnet

function Resolve-Dotnet {
    $candidates = @()
    $onPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if ($onPath) { $candidates += $onPath }
    $userScope = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    if (Test-Path $userScope) { $candidates += $userScope }

    foreach ($candidate in $candidates) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            # Framework-dependent exes (the driver) need DOTNET_ROOT to find the runtime.
            $env:DOTNET_ROOT = Split-Path $candidate -Parent
            $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
            return $candidate
        }
    }

    throw "No .NET SDK found. Checked: $($candidates -join ', ')"
}
