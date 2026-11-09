# syntax=docker/dockerfile:1

# ============================================================
# WorkTime.Server — production image
#
# Контекст сборки — корень репозитория (см. docker-compose.yml):
#   docker build -f WorkTime.Server/Dockerfile .
#
# Стек сборки: React PWA -> dist, затем publish API, и всё это
# объединяется в одном runtime-образе, где сервер раздаёт PWA
# из wwwroot. Один контейнер = API + веб-клиент.
# ============================================================

# ---------- 1. Сборка React PWA ----------
# pwaClient.js импортирует SDK по пути ../../WorkTime.ClientSdk/,
# поэтому сохраняем относительно /src оба проекта как в репозитории.
FROM node:20-alpine AS web-build
WORKDIR /src
COPY WorkTime.WebReact/package.json WorkTime.WebReact/package-lock.json WorkTime.WebReact/
COPY WorkTime.ClientSdk/ WorkTime.ClientSdk/
WORKDIR /src/WorkTime.WebReact
RUN npm ci
COPY WorkTime.WebReact/ ./
RUN npm run build

# ---------- 2. Публикация API ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS server-build
WORKDIR /src
# Отдельный слой restore: не сбрасывается при правках исходников
COPY WorkTime.Server/WorkTime.Server.csproj WorkTime.Server/
RUN dotnet restore WorkTime.Server/WorkTime.Server.csproj
COPY WorkTime.Server/ WorkTime.Server/
RUN dotnet publish WorkTime.Server/WorkTime.Server.csproj \
        -c Release -o /publish --no-restore

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# QuestPDF работает поверх SkiaSharp, которому в Linux нужны fontconfig/freetype,
# иначе экспорт в PDF падает уже в рантайме. curl нужен healthcheck'у:
# в базовом aspnet-образе нет ни wget, ни curl.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 tzdata curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=server-build /publish ./
COPY --from=web-build /src/WorkTime.WebReact/dist ./wwwroot

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fsS http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "WorkTime.Server.dll"]
