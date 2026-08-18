# Builds the single-file, self-contained release: dist\KeyPeek.exe
# Requires only the .NET 8 SDK. Run from anywhere.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\_dotnet.ps1"
$dotnet = Resolve-Dotnet

dotnet publish "$root\src\KeyPeek\KeyPeek.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$root\dist"
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = Get-Item "$root\dist\KeyPeek.exe"
Write-Host ""
Write-Host ("Published {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))
