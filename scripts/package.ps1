# Builds the thing you hand to someone else: dist\KeyPeek-<version>-win-x64.zip
#
# Inside the zip: the app (one self-contained exe — the other machine needs no .NET),
# Install.cmd / Uninstall.cmd for people who don't use PowerShell, the scripts those call,
# and a README in Vietnamese and English that says what to expect from SmartScreen.
#
# Run:  powershell -ExecutionPolicy Bypass -File scripts\package.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\_dotnet.ps1"
$dotnet = Resolve-Dotnet

# Version comes from the project file, so the zip name can never drift from the binary.
[xml]$proj = Get-Content "$root\src\KeyPeek\KeyPeek.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "no <Version> in KeyPeek.csproj" }

Write-Host "Packaging KeyPeek $version"
& "$PSScriptRoot\publish.ps1" | Out-Host
$exe = "$root\dist\KeyPeek.exe"
if (-not (Test-Path $exe)) { throw "publish did not produce $exe" }

$stage = "$root\dist\package"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item $exe $stage
Copy-Item "$PSScriptRoot\install.ps1" $stage
Copy-Item "$PSScriptRoot\uninstall.ps1" $stage

# Double-clickable wrappers. Unblock-File first: files extracted from a downloaded zip
# carry the mark-of-the-web, and PowerShell refuses to run those scripts by default.
@'
@echo off
echo Installing KeyPeek...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0*' -Recurse | Unblock-File"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
'@ | Set-Content "$stage\Install.cmd" -Encoding ASCII

@'
@echo off
echo Removing KeyPeek...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
echo (Your shortcuts and settings in %%APPDATA%%\KeyPeek were kept.)
echo  To delete those too: uninstall.ps1 -RemoveData
echo.
pause
'@ | Set-Content "$stage\Uninstall.cmd" -Encoding ASCII

$readme = @"
KeyPeek $version — keyboard shortcut overlay for Windows
=========================================================

Giữ phím Ctrl (hoặc Win / Alt) khoảng nửa giây: KeyPeek hiện bảng phím tắt của
đúng ứng dụng bạn đang dùng. Thả phím ra là bảng biến mất. Bấm vào một dòng để
KeyPeek gõ hộ tổ hợp đó.

CÀI ĐẶT
  1. Giải nén cả thư mục này ra một chỗ bất kỳ.
  2. Bấm đúp Install.cmd
  3. Windows có thể hiện cảnh báo "Windows protected your PC" — xem mục dưới.

  Cài cho từng người dùng, KHÔNG cần quyền admin. App được chép vào
  %LOCALAPPDATA%\Programs\KeyPeek và tạo lối tắt trong Start Menu.

CẢNH BÁO SMARTSCREEN
  File chưa được ký số (chữ ký số phải mua hằng năm), nên lần chạy đầu Windows
  sẽ hỏi. Chọn "More info" → "Run anyway". Nếu bạn không tin tưởng nguồn file
  này thì đừng chạy — đó là lời khuyên đúng cho mọi file .exe.

GỠ CÀI ĐẶT
  Bấm đúp Uninstall.cmd (giữ lại cấu hình), hoặc chạy:
    powershell -ExecutionPolicy Bypass -File uninstall.ps1 -RemoveData
  để xoá cả cấu hình và thư viện phím tắt của bạn.

YÊU CẦU
  Windows 10 phiên bản 1809 trở lên, 64-bit. KHÔNG cần cài .NET — mọi thứ nằm
  trong file exe.

APP LÀM GÌ VỚI BÀN PHÍM
  KeyPeek theo dõi phím bổ trợ (Ctrl/Win/Alt/Shift) để biết khi nào bạn giữ.
  Nó KHÔNG ghi lại những gì bạn gõ, và không gửi bất cứ thứ gì ra mạng ngoài
  việc tải cập nhật thư viện phím tắt (tính năng này có thể tắt trong Settings).
  Dữ liệu của bạn nằm ở %APPDATA%\KeyPeek.

-----------------------------------------------------------------------------

Hold Ctrl (or Win / Alt) for about half a second and KeyPeek shows the keyboard
shortcuts of the app you are in. Let go and it disappears. Click a row and
KeyPeek presses that shortcut for you.

INSTALL
  1. Extract this whole folder somewhere.
  2. Double-click Install.cmd
  3. Windows may warn that it "protected your PC" — see below.

  Per-user install, no administrator rights. The app is copied to
  %LOCALAPPDATA%\Programs\KeyPeek with a Start-Menu shortcut.

SMARTSCREEN
  The executable is not code-signed (certificates cost money every year), so
  Windows asks the first time: "More info" → "Run anyway". If you do not trust
  where you got this file, don't run it — that is the right rule for any .exe.

UNINSTALL
  Double-click Uninstall.cmd (keeps your settings), or run
    powershell -ExecutionPolicy Bypass -File uninstall.ps1 -RemoveData
  to remove your settings and your own shortcut library as well.

REQUIREMENTS
  Windows 10 1809 or later, 64-bit. No .NET installation needed.

WHAT IT DOES WITH YOUR KEYBOARD
  KeyPeek watches for held modifier keys so it knows when to appear. It does not
  record what you type, and nothing leaves the machine except the optional
  shortcut-library update download (switchable off in Settings). Your data lives
  in %APPDATA%\KeyPeek.

MIT licensed. Shortcut data derived from Microsoft PowerToys (MIT).
"@
$readme | Set-Content "$stage\README.txt" -Encoding UTF8

$zip = "$root\dist\KeyPeek-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

# Retry: a 63 MB executable written seconds ago is often still being scanned by real-time
# antivirus, and Compress-Archive fails outright when it cannot open the file.
$attempt = 0
while ($true) {
    try {
        Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
        break
    }
    catch {
        $attempt++
        if ($attempt -ge 6) { throw }
        Write-Host "  zip attempt $attempt failed (file still locked), retrying..."
        Start-Sleep -Seconds 5
        if (Test-Path $zip) { Remove-Item $zip -Force }
    }
}
Remove-Item $stage -Recurse -Force

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Package: $zip ($mb MB)"
Write-Host "Hand that single file to another machine: extract, run Install.cmd."
