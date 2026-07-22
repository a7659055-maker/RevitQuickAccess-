# Deploys RevitQuickAccess into the per-user Revit 2026 Addins folder.
# Works even while Revit is running: a first install just copies; for a re-install the loaded DLL
# is renamed aside (allowed on Windows) and the new one takes its place. Revit picks it up on restart.

$ErrorActionPreference = "Stop"

$proj    = $PSScriptRoot
$build   = Join-Path $proj "bin\Release\RevitQuickAccess.dll"
$addin   = Join-Path $proj "RevitQuickAccess.addin"
$target  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026"

if (-not (Test-Path $build)) { throw "Не найден build: $build. Сначала: dotnet build -c Release" }

New-Item -ItemType Directory -Force -Path $target | Out-Null

# clean leftovers from previous locked installs
Get-ChildItem $target -Filter "*.old_*" -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-Item $_.FullName -Force } catch {}
}

function Copy-Safe($src, $dst) {
    try {
        Copy-Item $src -Destination $dst -Force
    } catch {
        # locked by a running Revit → move aside, then copy
        $aside = "$dst.old_" + [DateTime]::Now.Ticks
        Move-Item $dst $aside -Force
        Copy-Item $src -Destination $dst -Force
    }
}

Copy-Safe $build (Join-Path $target "RevitQuickAccess.dll")
Copy-Safe $addin (Join-Path $target "RevitQuickAccess.addin")

$pdb = Join-Path $proj "bin\Release\RevitQuickAccess.pdb"
if (Test-Path $pdb) { try { Copy-Safe $pdb (Join-Path $target "RevitQuickAccess.pdb") } catch {} }

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Host "Установлено в: $target (Revit запущен — перезапусти его, чтобы плагин загрузился)" -ForegroundColor Yellow
} else {
    Write-Host "Установлено в: $target" -ForegroundColor Green
}
Get-ChildItem $target -Filter "RevitQuickAccess.*" | Select-Object Name, Length, LastWriteTime
