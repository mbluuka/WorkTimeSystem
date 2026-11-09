# Распределённая система учёта рабочего времени

Local-first система учёта рабочего времени с офлайн-синхронизацией, поддержкой ролевой модели и кроссплатформенными клиентами.

## Архитектура

```
┌─────────────────────────────────────────────────┐
│                   Сервер (C#/.NET 8)             │
│  ASP.NET Core Web API + PostgreSQL + EF Core    │
│  JWT-аутентификация, идемпотентность, SyncQueue │
└──────────────────┬──────────────────────────────┘
                   │
     ┌─────────────┼─────────────┐
     │             │             │
     ▼             ▼             ▼
┌─────────┐  ┌──────────┐  ┌───────────┐
│ React   │  │ Console  │  │ (Flutter) │
│ PWA     │  │ Client   │  │  (план)   │
│ (веб)   │  │ (C#)     │  │           │
└─────────┘  └──────────┘  └───────────┘
  SQLite/      SQLite
  IndexedDB    (локальная БД)
```

## Технологии

| Компонент | Технология |
|-----------|-----------|
| Сервер | C# / .NET 8 / ASP.NET Core |
| БД сервера | PostgreSQL 17 (через EF Core + Npgsql) |
| Аутентификация | JWT (BCrypt для хеширования паролей) |
| Веб-клиент | React 18 + Vite (PWA, IndexedDB) |
| Консольный клиент | C# / .NET 8 / SQLite (EF Core) |
| Экспорт PDF | QuestPDF |
| Экспорт XLSX | ClosedXML |
| Контейнеризация | Docker Compose (PostgreSQL + сервер + PWA) |

## Ролевая модель

| Роль | Возможности |
|------|------------|
| **Employee** | Отметка начала/конца дня, перерывы, просмотр своих отчётов, ручная корректировка записей |
| **Manager** | Все возможности Employee + просмотр команды, сводные отчёты, экспорт PDF/XLSX |
| **Admin** | Все возможности Manager + управление пользователями (CRUD, блокировка), проекты, аудит-логи |

---

## Быстрый старт

### Способ 1: Docker (рекомендуемый)

Один контейнер = API + React PWA. База данных — отдельный контейнер PostgreSQL.

```bash
docker compose up --build
```

Сервер будет доступен на **http://localhost:8080** (Swagger: `/swagger`).

При первом запуске автоматически:
- Применяются миграции EF Core
- Создаются seed-пользователи:
  - **Админ:** `admin@worktime.local` / `Admin@123`
  - **Сотрудник:** `employee@worktime.local` / `Employee@123`

Остановить всё:
```bash
docker compose down
```

### Способ 2: Локальная разработка (PowerShell / Bash)

**Требования:** Docker Desktop, .NET 8 SDK, Node 18+

#### Windows (PowerShell)
```powershell
.\start-dev.ps1
```

#### Linux / macOS (Bash)
```bash
chmod +x start-dev.sh
./start-dev.sh
```

Запускает параллельно:
- PostgreSQL через `docker compose up -d postgres`
- .NET сервер (горячая перезагрузка)
- Vite dev-сервер с проксированием `/api` на сервер

Сервисы доступны на:
| Сервис | URL |
|--------|-----|
| React PWA | http://localhost:5173 |
| .NET Server | http://localhost:5114 |
| Swagger | http://localhost:5114/swagger |
| PostgreSQL | localhost:15432 |

Остановка: `Ctrl+C` — скрипт аккуратно остановит все сервисы.

---

## Ручной запуск (покомпонентно)

### 1. PostgreSQL через Docker

```bash
docker compose up -d postgres
```

База данных:
- Хост: `localhost:15432`
- БД: `worktime`
- Пользователь: `worktime`
- Пароль: `worktime_dev_password`

### 2. Сервер

```bash
cd WorkTime.Server
dotnet run
```

Сервер запускается на `https://localhost:7132` / `http://localhost:5114`.

При первом запуске автоматически применяются миграции EF Core и создаются seed-пользователи.

### 3. Веб-клиент (React PWA)

```bash
cd WorkTime.WebReact
npm install
npm run dev      # режим разработки (http://localhost:5173)
npm run build    # сборка в dist/
```

Сервер автоматически раздаёт собранную PWA из `WorkTime.WebReact/dist/` (при наличии папки).

### 4. Консольный клиент

```bash
cd WorkTime.Client
dotnet run
```

При запуске потребуется ввести email и пароль (используйте seed-учётные данные).

---

## API Endpoints

### Аутентификация
| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/api/auth/register` | Регистрация |
| POST | `/api/auth/login` | Вход, получение JWT |

### Рабочие события
| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/api/workevents` | Создать событие |
| GET | `/api/workevents` | Получить все события |
| GET | `/api/workevents/{id}` | Получить по ID |
| GET | `/api/workevents/user/{userId}` | События пользователя |
| GET | `/api/workevents/sync` | Синхронизация по дате |
| GET | `/api/workevents/events/since` | События с ServerOrder |
| POST | `/api/workevents/batch` | Пакетная синхронизация |

### Синхронизация
| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/api/sync/operations` | Отправка операций в очередь |

### Устройства
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/devices` | Список устройств |
| POST | `/api/devices` | Регистрация устройства |
| DELETE | `/api/devices/{id}` | Удаление устройства |

### Пользователи (Admin)
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/users` | Список пользователей |
| POST | `/api/users` | Создание |
| PUT | `/api/users/{id}` | Обновление |
| DELETE | `/api/users/{id}` | Удаление |
| POST | `/api/users/{id}/block` | Блокировка |
| POST | `/api/users/{id}/unblock` | Разблокировка |

### Проекты
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/projects` | Список |
| POST | `/api/projects` | Создание |
| PUT | `/api/projects/{id}` | Обновление |
| DELETE | `/api/projects/{id}` | Удаление |

### Временные записи
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/timeentries` | Список (с фильтрами) |
| POST | `/api/timeentries` | Создание |
| PUT | `/api/timeentries/{id}` | Обновление |
| DELETE | `/api/timeentries/{id}` | Удаление |

### Отчёты
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/reports/summary` | Сводка по пользователю |
| GET | `/api/reports/team` | Сводка по команде (Manager+) |
| GET | `/api/reports/export/pdf` | Экспорт PDF |
| GET | `/api/reports/export/xlsx` | Экспорт XLSX |

### Аудит (Admin)
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/auditlogs` | Журнал действий |

## Local-first архитектура

1. **Офлайн-приоритет:** все действия сохраняются в локальную БД (SQLite / IndexedDB)
2. **Очередь синхронизации (SyncQueue):** операции накапливаются и отправляются при восстановлении соединения
3. **Идемпотентность:** повторная отправка не создаёт дубликатов (через `Idempotency-Key` и проверку ID)
4. **Разрешение конфликтов:** Last Write Wins (по временной метке)
5. **ServerOrder:** монотонная последовательность на сервере для упорядочивания событий

## Структура проекта

```
WorkTimeSystem/
├── WorkTime.Server/              # Сервер API
│   ├── Controllers/              # Auth, WorkEvents, Sync, Users, Projects, etc.
│   ├── Models/                   # User, WorkEvent, Project, AuditLog, Report, etc.
│   ├── DTOs/                     # Request/Response объекты
│   ├── Services/                 # SyncQueueProcessor, BulkUpsert, Idempotency, DbSeeder
│   ├── Middleware/               # IdempotencyMiddleware
│   ├── Data/                     # WorkTimeDbContext
│   ├── Migrations/               # EF Core миграции
│   └── Dockerfile                # Контейнер образа сервера
├── WorkTime.Client/              # Консольный клиент (C#, SQLite)
│   ├── Models/                   # LocalWorkEvent
│   ├── Services/                 # SyncService
│   └── Storage/                  # LocalEventStore
├── WorkTime.ClientSdk/           # PWA SDK (IndexedDB, синхронизация)
├── WorkTime.WebReact/            # React PWA
│   └── src/
│       ├── components/           # Login, Dashboard, ManagerView, AdminView
│       ├── api.js                # API-клиент с JWT
│       └── pwaClient.js          # Мост к SDK
├── docker-compose.yml            # Docker: PostgreSQL + сервер
├── start-dev.ps1                 # Windows: локальный запуск всех сервисов
├── start-dev.sh                  # Linux/macOS: локальный запуск всех сервисов
└── .dockerignore                 # Игнор для docker build
```

## Тестирование

```bash
# Сервер
dotnet build WorkTime.Server/WorkTime.Server.csproj

# Клиент
dotnet build WorkTime.Client/WorkTime.Client.csproj

# Веб-клиент
cd WorkTime.WebReact && npm run build
```