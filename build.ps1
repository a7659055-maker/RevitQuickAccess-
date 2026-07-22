# Builds the plugin (Release) and the single-file installer, then drops the setup exe in the project root.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "[1/3] Building plugin..." -ForegroundColor Cyan
dotnet build (Join-Path $root "RevitQuickAccess.csproj") -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }

Write-Host "[2/3] Publishing installer..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "Installer\RqaInstaller.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -v minimal
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

Write-Host "[3/3] Copying setup exe to project root..." -ForegroundColor Cyan
$exe = Join-Path $root "Installer\bin\Release\net8.0-windows\win-x64\publish\RevitQuickAccess-Setup.exe"
Copy-Item $exe (Join-Path $root "RevitQuickAccess-Setup.exe") -Force

Write-Host "Done. Run RevitQuickAccess-Setup.exe (with Revit closed) to install." -ForegroundColor Green
