# Выпуск новой версии: поднимает версию, собирает, коммитит, ставит тег и публикует релиз на GitHub.
#
#   .\release.ps1 -Version 1.1.0
#
# Плагин при старте Revit сам увидит новый релиз и молча обновится (см. Update/UpdateService.cs).
# GitHub Actions здесь не используется намеренно: сборка требует RevitAPI.dll, которых нет в
# открытом доступе, поэтому релиз собирается локально.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "RevitQuickAccess.csproj"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Версия должна быть вида 1.2.3" }

Write-Host "[1/5] Версия -> $Version" -ForegroundColor Cyan
$text = Get-Content $csproj -Raw
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
Set-Content $csproj $text -NoNewline

Write-Host "[2/5] Сборка" -ForegroundColor Cyan
& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась" }

$dll = Join-Path $root "bin\Release\RevitQuickAccess.dll"
$exe = Join-Path $root "RevitQuickAccess-Setup.exe"
if (-not (Test-Path $dll)) { throw "Не найден $dll" }

Write-Host "[3/5] Коммит и тег" -ForegroundColor Cyan
git -C $root add -A
git -C $root commit -m "Release v$Version" | Out-Null
git -C $root tag "v$Version"

Write-Host "[4/5] Push" -ForegroundColor Cyan
git -C $root push origin HEAD --tags

Write-Host "[5/5] Релиз на GitHub" -ForegroundColor Cyan
if (Get-Command gh -ErrorAction SilentlyContinue) {
    $args = @("release", "create", "v$Version", $dll, $exe, "--title", "v$Version")
    if ($Notes) { $args += @("--notes", $Notes) } else { $args += "--generate-notes" }
    gh @args
    Write-Host "Готово. Плагин у пользователей обновится при следующем запуске Revit." -ForegroundColor Green
} else {
    Write-Host "GitHub CLI (gh) не установлен — создай релиз v$Version вручную и приложи файлы:" -ForegroundColor Yellow
    Write-Host "  $dll"
    Write-Host "  $exe"
    Write-Host "Установить CLI:  winget install GitHub.cli" -ForegroundColor Yellow
}
