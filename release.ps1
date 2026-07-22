# Выпуск новой версии.
#
#   .\release.ps1 -Fix         # пакет исправлений:  1.2.3 -> 1.2.4
#   .\release.ps1 -Feature     # новая функция:      1.2.3 -> 1.3.0
#   .\release.ps1 -Version 2.0.0
#
# Схема версий  MAJOR.FEATURE.FIX
#   вторая цифра — появилась новая функция
#   третья цифра — пакет исправлений багов
#
# Плагин у всех пользователей увидит релиз при старте Revit, молча скачает его и установит
# при закрытии Revit (см. Update/UpdateService.cs).
#
# GitHub Actions намеренно не используется: сборке нужны RevitAPI.dll, которых нет в открытом
# доступе, поэтому релиз собирается локально.

param(
    [switch]$Feature,
    [switch]$Fix,
    [string]$Version,
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "RevitQuickAccess.csproj"

$text = Get-Content $csproj -Raw
if ($text -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') { throw "Не нашёл <Version> в csproj" }
$cur = [int[]]@($Matches[1], $Matches[2], $Matches[3])

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Версия должна быть вида 1.2.3" }
    $new = $Version
} elseif ($Feature) {
    $new = "{0}.{1}.0" -f $cur[0], ($cur[1] + 1)
} elseif ($Fix) {
    $new = "{0}.{1}.{2}" -f $cur[0], $cur[1], ($cur[2] + 1)
} else {
    throw "Укажи -Fix (исправления), -Feature (новая функция) или -Version"
}

Write-Host "[1/5] Версия $($cur -join '.') -> $new" -ForegroundColor Cyan
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$new</Version>")
Set-Content $csproj $text -NoNewline

# установщик показывает свою версию в шапке — держим её в синхроне с плагином
$inst = Join-Path $root "Installer\RqaInstaller.csproj"
if (Test-Path $inst) {
    $it = Get-Content $inst -Raw
    if ($it -match '<Version>[^<]*</Version>') {
        Set-Content $inst ([regex]::Replace($it, '<Version>[^<]*</Version>', "<Version>$new</Version>")) -NoNewline
    }
}

Write-Host "[2/5] Сборка" -ForegroundColor Cyan
& (Join-Path $root "build.ps1") -NoCommit
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась" }

$dll = Join-Path $root "bin\Release\RevitQuickAccess.dll"
$exe = Join-Path $root "RevitQuickAccess-Setup.exe"
if (-not (Test-Path $dll)) { throw "Не найден $dll" }

Write-Host "[3/5] Коммит и тег" -ForegroundColor Cyan
git -C $root add -A
git -C $root commit -m "Release v$new" | Out-Null
git -C $root tag "v$new"

Write-Host "[4/5] Push" -ForegroundColor Cyan
git -C $root push origin HEAD --tags

Write-Host "[5/5] Релиз на GitHub" -ForegroundColor Cyan
# gh может быть не в PATH текущей сессии (например сразу после установки) — ищем и по обычному пути
$gh = (Get-Command gh -ErrorAction SilentlyContinue)?.Source
if (-not $gh) {
    foreach ($p in @("$env:ProgramFiles\GitHub CLI\gh.exe", "${env:ProgramFiles(x86)}\GitHub CLI\gh.exe",
                     "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe")) {
        if (Test-Path $p) { $gh = $p; break }
    }
}
if ($gh) {
    $ghArgs = @("release", "create", "v$new", $dll, $exe, "--title", "v$new")
    if ($Notes) { $ghArgs += @("--notes", $Notes) } else { $ghArgs += "--generate-notes" }
    & $gh @ghArgs
    Write-Host "Готово. Обновление разъедется пользователям при следующем запуске Revit." -ForegroundColor Green
} else {
    Write-Host "GitHub CLI (gh) не установлен — создай релиз v$new вручную и приложи:" -ForegroundColor Yellow
    Write-Host "  $dll"
    Write-Host "  $exe"
    Write-Host "Установить:  winget install GitHub.cli" -ForegroundColor Yellow
}
