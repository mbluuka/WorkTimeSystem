#requires -Version 5.1
<#
.SYNOPSIS
    Запуск всех сервисов WorkTime System для локальной разработки.
.DESCRIPTION
    Запускает параллельно:
      1. PostgreSQL (через docker compose)
      2. .NET сервер (dotnet run)
      3. Vite dev-сервер React PWA (проксирует /api на сервер)
    При нажатии Ctrl+C останавливает дочерние процессы.
    PostgreSQL остаётся запущенным — данные синхронизации не теряются,
    а повторный старт не ждёт инициализацию тома. Для остановки БД:
        docker compose stop postgres
.EXAMPLE
    .\start-dev.ps1
.NOTES
    Если скрипт не запускается из-за политики выполнения:
        powershell -ExecutionPolicy Bypass -File .\start-dev.ps1
#>

$ErrorActionPreference = "Stop"
$host.ui.RawUI.WindowTitle = "WorkTime System - Dev"

$root = $PSScriptRoot
$reactDir = Join-Path $root "WorkTime.WebReact"
$serverDir = Join-Path $root "WorkTime.Server"

$script:dotnetJob = $null
$script:viteJob = $null

function Write-Step  { param([string]$Message) Write-Host "  -> $Message" -ForegroundColor Yellow }
function Write-Ok    { param([string]$Message) Write-Host "  [$Message]" -ForegroundColor Green }
function Write-Dim   { param([string]$Message) Write-Host "     $Message" -ForegroundColor DarkGray }

function Stop-DevProcesses {
    Write-Host ""
    Write-Host "  [!] Остановка сервисов..." -ForegroundColor Yellow
    # vite запускается через cmd /c npm, поэтому снимаем всё дерево процессов
    foreach ($job in @($script:viteJob, $script:dotnetJob)) {
        if ($job -and -not $job.HasExited) {
            & taskkill /PID $job.Id /T /F 2>$null | Out-Null
        }
    }
    Write-Host "  [done] Сервисы остановлены. PostgreSQL оставлен запущенным." -ForegroundColor Green
    Write-Host "         Полная остановка БД: docker compose down" -ForegroundColor DarkGray
    Write-Host ""
}

Write-Host ""
Write-Host "  ==========================================" -ForegroundColor Cyan
Write-Host "    WorkTime System - Dev Environment" -ForegroundColor Cyan
Write-Host "  ==========================================" -ForegroundColor Cyan
Write-Host ""

# ---------- Проверка зависимостей ----------
Write-Step "Проверка зависимостей"

$missing = @()
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    $missing += @{ Cmd = "docker"; Hint = "Установите Docker Desktop: https://www.docker.com/products/docker-desktop/" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $missing += @{ Cmd = "dotnet"; Hint = "Установите .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" }
}
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    $missing += @{ Cmd = "npm"; Hint = "Установите Node.js 18+: https://nodejs.org" }
}

if ($missing.Count -gt 0) {
    foreach ($m in $missing) {
        Write-Host "  [x] '$($m.Cmd)' не найден в PATH" -ForegroundColor Red
        Write-Dim $m.Hint
    }
    Write-Host ""
    exit 1
}

# Docker Desktop может быть установлен, но не запущен
docker info 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [x] Docker запущен, но демон недоступен." -ForegroundColor Red
    Write-Dim "Откройте Docker Desktop и дождитесь его запуска, затем повторите."
    Write-Host ""
    exit 1
}

Write-Ok "зависимости в порядке"

# ---------- 1. PostgreSQL ----------
Write-Host ""
Write-Step "1/3  PostgreSQL"
Push-Location $root
try {
    docker compose up -d postgres
    if ($LASTEXITCODE -ne 0) { throw "docker compose up завершился с кодом $LASTEXITCODE" }

    # dotnet run сам применит миграции, но только когда БД примет соединения —
    # иначе старт сервера упрётся в таймаут и миграции не применятся.
    $deadline = (Get-Date).AddSeconds(60)
    $healthy = $false
    while ((Get-Date) -lt $deadline) {
        $state = docker inspect -f "{{.State.Health.Status}}" worktime-postgres 2>$null
        if ($state -eq "healthy") { $healthy = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $healthy) {
        Write-Host "  [x] PostgreSQL не перешёл в состояние healthy за 60 c." -ForegroundColor Red
        Write-Dim "Проверьте: docker compose logs postgres"
        exit 1
    }
    Write-Ok "postgres healthy (localhost:15432)"
}
finally {
    Pop-Location
}

# ---------- 2. .NET Server ----------
Write-Step "2/3  .NET сервер (http://localhost:5114)"
$script:dotnetJob = Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory $serverDir -PassThru

# ---------- 3. React Dev Server ----------
Write-Step "3/3  React PWA (http://localhost:5173)"
if (-not (Test-Path (Join-Path $reactDir "node_modules"))) {
    Write-Dim "node_modules отсутствует - выполняется npm install..."
    Push-Location $reactDir
    try { npm install } finally { Pop-Location }
}
$script:viteJob = Start-Process -FilePath "cmd" -ArgumentList "/c", "npm run dev" -WorkingDirectory $reactDir -PassThru

Write-Host ""
Write-Host "  ==========================================" -ForegroundColor Green
Write-Host "    Все сервисы запущены" -ForegroundColor Green
Write-Host "  ==========================================" -ForegroundColor Green
Write-Host ""
Write-Dim "React PWA     http://localhost:5173"
Write-Dim ".NET Server   http://localhost:5114"
Write-Dim "Swagger       http://localhost:5114/swagger"
Write-Dim "PostgreSQL    localhost:15432  (db: worktime)"
Write-Host ""
Write-Dim "Seed-доступы: admin@worktime.local / Admin@123"
Write-Dim "              employee@worktime.local / Employee@123"
Write-Host ""
Write-Host "  Нажмите Ctrl+C для остановки.`n" -ForegroundColor Yellow

try {
    # Пока живёт dotnet-процесс, считаем окружение рабочим.
    Wait-Process -Id $script:dotnetJob.Id
}
catch {
    # Ctrl+C приходит сюда — штатный сценарий остановки
}
finally {
    Stop-DevProcesses
}
