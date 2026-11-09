#!/usr/bin/env bash
# ============================================================
# WorkTime System — запуск всех сервисов для локальной разработки
#
#   1. PostgreSQL (docker compose)
#   2. .NET сервер (dotnet run)
#   3. Vite dev-сервер React PWA (проксирует /api на сервер)
#
# Ctrl+C останавливает дочерние процессы; PostgreSQL остаётся
# запущенным. Полная остановка: docker compose down
#
# Запуск: ./start-dev.sh
# ============================================================
set -uo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; GRAY='\033[0;90m'; CYAN='\033[0;36m'; NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/WorkTime.Server"
REACT_DIR="$SCRIPT_DIR/WorkTime.WebReact"

DOTNET_PID=""
NPM_PID=""

step() { printf "  %s->%s %s\n" "$YELLOW" "$NC" "$1"; }
ok()   { printf "  %s[%s]%s %s\n" "$GREEN" "done" "$NC" "$1"; }
dim()  { printf "     %s%s%s\n" "$GRAY" "$1" "$NC"; }
fail() { printf "  %s[x]%s %s\n" "$RED" "$NC" "$1"; }

stop_dev() {
    printf "\n  %s[!]%s Остановка сервисов...\n" "$YELLOW" "$NC"
    [ -n "$NPM_PID" ]    && kill -- "-$(ps -o pgid= "$NPM_PID" | tr -d ' ')" 2>/dev/null
    [ -n "$DOTNET_PID" ] && kill -- "-$(ps -o pgid= "$DOTNET_PID" | tr -d ' ')" 2>/dev/null
    wait 2>/dev/null
    ok "сервисы остановлены. PostgreSQL оставлен запущенным."
    dim "полная остановка БД: docker compose down"
    printf "\n"
    exit 0
}
trap stop_dev INT TERM

printf "\n  %s==========================================%s\n" "$CYAN" "$NC"
printf "    %sWorkTime System - Dev Environment%s\n" "$CYAN" "$NC"
printf "  %s==========================================%s\n\n" "$CYAN" "$NC"

# ---------- Проверка зависимостей ----------
step "Проверка зависимостей"
missing=0
command -v docker >/dev/null 2>&1 || { fail "docker не найден — https://www.docker.com/products/docker-desktop/"; missing=1; }
command -v dotnet >/dev/null 2>&1 || { fail "dotnet не найден — .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"; missing=1; }
command -v npm    >/dev/null 2>&1 || { fail "npm не найден — Node.js 18+: https://nodejs.org"; missing=1; }
[ "$missing" -eq 1 ] && exit 1

if ! docker info >/dev/null 2>&1; then
    fail "Docker установлен, но демон недоступен — запустите Docker Desktop"
    exit 1
fi
ok "зависимости в порядке"

# ---------- 1. PostgreSQL ----------
printf "\n"
step "1/3  PostgreSQL"
cd "$SCRIPT_DIR"
docker compose up -d postgres || { fail "не удалось запустить PostgreSQL"; exit 1; }

# dotnet run применит миграции только когда БД примет соединения
deadline=$((SECONDS + 60))
healthy=0
while [ "$SECONDS" -lt "$deadline" ]; do
    if [ "$(docker inspect -f '{{.State.Health.Status}}' worktime-postgres 2>/dev/null)" = "healthy" ]; then
        healthy=1; break
    fi
    sleep 2
done
[ "$healthy" -eq 1 ] || { fail "PostgreSQL не стал healthy за 60 c — docker compose logs postgres"; exit 1; }
ok "postgres healthy (localhost:15432)"

# ---------- 2. .NET Server ----------
step "2/3  .NET сервер (http://localhost:5114)"
(cd "$SERVER_DIR" && dotnet run) &
DOTNET_PID=$!

# ---------- 3. React Dev Server ----------
step "3/3  React PWA (http://localhost:5173)"
if [ ! -d "$REACT_DIR/node_modules" ]; then
    dim "node_modules отсутствует — npm install..."
    (cd "$REACT_DIR" && npm install)
fi
(cd "$REACT_DIR" && npm run dev) &
NPM_PID=$!

printf "\n  %s==========================================%s\n" "$GREEN" "$NC"
printf "    %sВсе сервисы запущены%s\n" "$GREEN" "$NC"
printf "  %s==========================================%s\n\n" "$GREEN" "$NC"
dim "React PWA     http://localhost:5173"
dim ".NET Server   http://localhost:5114"
dim "Swagger       http://localhost:5114/swagger"
dim "PostgreSQL    localhost:15432  (db: worktime)"
printf "\n"
dim "Seed-доступы: admin@worktime.local / Admin@123"
dim "              employee@worktime.local / Employee@123"
printf "\n"
printf "  %sНажмите Ctrl+C для остановки.%s\n\n" "$YELLOW" "$NC"

# Пока живёт dotnet-процесс, считаем окружение рабочим
wait "$DOTNET_PID"
stop_dev