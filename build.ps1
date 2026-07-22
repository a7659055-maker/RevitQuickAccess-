# Собирает плагин (Release) и однофайловый установщик, кладёт setup рядом с проектом.
# По умолчанию делает авто-коммит изменений (отключается ключом -NoCommit).

param([switch]$NoCommit)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "[1/4] Сборка плагина..." -ForegroundColor Cyan
dotnet build (Join-Path $root "RevitQuickAccess.csproj") -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }

Write-Host "[2/4] Сборка установщика..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "Installer\RqaInstaller.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -v minimal
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

Write-Host "[3/4] Копирование setup..." -ForegroundColor Cyan
$exe = Join-Path $root "Installer\bin\Release\net8.0-windows\win-x64\publish\RevitQuickAccess-Setup.exe"
Copy-Item $exe (Join-Path $root "RevitQuickAccess-Setup.exe") -Force

if (-not $NoCommit) {
    Write-Host "[4/4] Авто-коммит..." -ForegroundColor Cyan
    git -C $root add -A
    $staged = git -C $root diff --cached --name-only
    if ($staged) {
        $stamp = Get-Date -Format "yyyy-MM-dd HH:mm"
        git -C $root commit -m "build: $stamp" | Out-Null
        Write-Host "  закоммичено: $((($staged -split "`n").Count)) файл(ов)" -ForegroundColor DarkGray
    } else {
        Write-Host "  изменений нет" -ForegroundColor DarkGray
    }
} else {
    Write-Host "[4/4] Коммит пропущен (-NoCommit)" -ForegroundColor DarkGray
}

Write-Host "Готово. Запусти RevitQuickAccess-Setup.exe для установки." -ForegroundColor Green
