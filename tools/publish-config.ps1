# Публикует твой рабочий конфиг в репозиторий как пресет.
#
#   .\publish-config.ps1                  # пресет "default"
#   .\publish-config.ps1 -Name mep-vk     # пресет с именем
#
# Забирает живые конфиги из папки Revit Addins и кладёт в presets/<Name>/,
# чтобы их можно было закоммитить и раздать (или залить в релиз).
# Личные логи, отчёты и иконки не копируются.

param([string]$Name = "default")

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$live = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026"
$dest = Join-Path $repo "presets\$Name"

if (-not (Test-Path $live)) { throw "Не найдена папка плагина: $live" }
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$files = @(
    "RevitQuickAccess_bindsets.txt",   # наборы биндов
    "RevitQuickAccess_quick.txt",      # плитки быстрых команд
    "RevitQuickAccess_settings.txt"    # настройки инструментов
)

$copied = 0
foreach ($f in $files) {
    $src = Join-Path $live $f
    if (Test-Path $src) { Copy-Item $src (Join-Path $dest $f) -Force; $copied++; Write-Host "  + $f" }
    else { Write-Host "  - $f (нет)" -ForegroundColor DarkGray }
}

# В настройках лежит личный URL приёмника отчётов — он не должен попасть в публичный репозиторий.
# Пишем через -Value: если после фильтра не осталось ни строки, пайплайн пустой и Set-Content
# вообще не вызывается — файл остался бы с секретом.
$s = Join-Path $dest "RevitQuickAccess_settings.txt"
if (Test-Path $s) {
    $kept = @(Get-Content $s | Where-Object { $_ -notmatch '^\s*reportEndpoint\s*=' })
    Set-Content -Path $s -Value $kept
    $left = @(Get-Content $s -ErrorAction SilentlyContinue | Where-Object { $_ -match 'reportEndpoint' })
    if ($left.Count -gt 0) { throw "reportEndpoint не вычистился из $s — публиковать нельзя" }
    Write-Host "  · reportEndpoint вычищен из настроек" -ForegroundColor DarkGray
}

# иконки плиток, чтобы пресет выглядел как надо
$icons = Join-Path $live "icons"
if (Test-Path $icons) {
    Copy-Item $icons (Join-Path $dest "icons") -Recurse -Force
    Write-Host "  + icons/"
}

Write-Host ""
Write-Host "Готово: $dest (файлов: $copied)" -ForegroundColor Green
Write-Host "Закоммить и запушь:" -ForegroundColor Green
Write-Host "    git add presets; git commit -m `"presets: $Name`"; git push" -ForegroundColor Green
Write-Host ""
Write-Host "Чтобы применить пресет у себя — скопируй файлы обратно в:" -ForegroundColor DarkGray
Write-Host "    $live" -ForegroundColor DarkGray
