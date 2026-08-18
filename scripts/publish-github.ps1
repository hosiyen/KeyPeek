# Publishes KeyPeek to GitHub: a clean public repo (no history - the working repo's
# history contains desktop screenshots) + a v0.9.0 release carrying the install zip.
# Prereq: `gh auth login` completed. Safe to re-run; each step skips what already exists.

$ErrorActionPreference = "Stop"
$gh = "$env:ProgramFiles\GitHub CLI\gh.exe"
$root = Split-Path $PSScriptRoot -Parent
$snap = Join-Path $env:TEMP "keypeek-public"
$zip = Join-Path $root "dist\KeyPeek-0.9.0-win-x64.zip"
$notes = Join-Path $root "dist\release-notes.md"

& $gh auth status | Out-Null

# --- clean snapshot: tracked files only, minus the UI-review screenshots ---
if (-not (Test-Path (Join-Path $snap ".git"))) {
    Remove-Item $snap -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $snap | Out-Null
    Push-Location $root
    git archive --format=zip HEAD -o "$env:TEMP\keypeek-archive.zip"
    Pop-Location
    Expand-Archive "$env:TEMP\keypeek-archive.zip" $snap -Force
    Remove-Item "$env:TEMP\keypeek-archive.zip" -Force
    Remove-Item (Join-Path $snap "docs\ui-review") -Recurse -Force -ErrorAction SilentlyContinue
    Push-Location $snap
    git init -b main | Out-Null
    # A fresh repo has no identity on this machine; use the GitHub noreply address so
    # the real email never appears in a public commit.
    git config user.name "hosiyen"
    git config user.email "hosiyen@users.noreply.github.com"
    git add -A
    git commit -m "KeyPeek 0.9.0" | Out-Null
    Pop-Location
}

# --- repo ---
$owner = (& $gh api user --jq .login).Trim()
$exists = $true
try { & $gh repo view "$owner/KeyPeek" 2>$null | Out-Null } catch { $exists = $false }
if (-not $exists) {
    & $gh repo create KeyPeek --public --description "Hold Ctrl/Win/Alt to see the shortcuts of the app you're in. Windows tray utility, MIT." | Out-Null
}
Push-Location $snap
if (-not (git remote | Select-String "origin")) {
    git remote add origin "https://github.com/$owner/KeyPeek.git"
}
git push -u origin main --force
Pop-Location

# --- release with the install zip ---
if (-not (Test-Path $zip)) { throw "Missing $zip - run scripts\package.ps1 first." }
$hasRelease = $true
try { & $gh release view v0.9.0 -R "$owner/KeyPeek" 2>$null | Out-Null } catch { $hasRelease = $false }
if (-not $hasRelease) {
    & $gh release create v0.9.0 $zip -R "$owner/KeyPeek" --title "KeyPeek 0.9.0" --notes-file $notes
}

Write-Host ""
Write-Host "Repo:    https://github.com/$owner/KeyPeek"
Write-Host "Release: https://github.com/$owner/KeyPeek/releases/tag/v0.9.0"
