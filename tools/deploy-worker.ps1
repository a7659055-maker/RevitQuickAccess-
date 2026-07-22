# Разворачивает приёмник отчётов об ошибках (Cloudflare Worker).
# Запускать из этой папки:   .\deploy-worker.ps1
#
# Что произойдёт:
#   1. откроется браузер — войдёшь в Cloudflare (бесплатный аккаунт, если ещё нет — зарегистрируйся там же)
#   2. воркер задеплоится, скрипт покажет его URL
#   3. попросит вставить токен Telegram-бота и chat id — они сохранятся НА СТОРОНЕ Cloudflare,
#      в репозиторий и в плагин не попадут
#
# Где взять токен и chat id:
#   токен  — написать @BotFather в Telegram, /newbot, он выдаст токен
#   chat id— написать своему боту любое сообщение, затем открыть в браузере
#            https://api.telegram.org/bot<ТОКЕН>/getUpdates  и взять "chat":{"id":ЧИСЛО}

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "== 1/4 Вход в Cloudflare (откроется браузер) ==" -ForegroundColor Cyan
npx --yes wrangler login

Write-Host "== 2/4 Публикация воркера ==" -ForegroundColor Cyan
npx --yes wrangler deploy

Write-Host "== 3/4 Токен Telegram-бота ==" -ForegroundColor Cyan
Write-Host "Вставь токен от @BotFather и нажми Enter:" -ForegroundColor Yellow
npx --yes wrangler secret put TELEGRAM_TOKEN

Write-Host "== 4/4 Chat ID ==" -ForegroundColor Cyan
Write-Host "Вставь свой chat id и нажми Enter:" -ForegroundColor Yellow
npx --yes wrangler secret put TELEGRAM_CHAT_ID

Write-Host ""
Write-Host "Готово. Скопируй URL воркера (строка вида https://rqa-report.<логин>.workers.dev выше)" -ForegroundColor Green
Write-Host "и пропиши его в %APPDATA%\Autodesk\Revit\Addins\2026\RevitQuickAccess_settings.txt:" -ForegroundColor Green
Write-Host "    reportEndpoint=https://rqa-report.<логин>.workers.dev" -ForegroundColor Green
