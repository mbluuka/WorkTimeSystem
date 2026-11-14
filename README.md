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
     ┌─────────────┼─────────────┬───────────┐
     │             │             │           │
     ▼             ▼             ▼           ▼
┌─────────┐  ┌──────────┐  ┌───────────┐  ┌──────────┐
│ React   │  │ Console  │  │  Desktop  │  │ (Flutter)│
│ PWA     │  │ Client   │  │ (Avalonia)│  │  (план)  │
│ (веб)   │  │ (C#)     │  │ (C#)      │  │          │
└─────────┘  └──────────┘  └───────────┘  └──────────┘
  SQLite/      SQLite      SQLite
  IndexedDB    (локальная БД) (локальная БД)
```

## Технологии

| Компонент | Технология |
|-----------|-----------|
| Сервер | C# / .NET 8 / ASP.NET Core |
| БД сервера | PostgreSQL 17 (через EF Core + Npgsql) |
| Аутентификация | JWT (BCrypt для хеширования паролей) |
| Веб-клиент | React 18 + Vite (PWA, IndexedDB) |
| Консольный клиент | C# / .NET 8 / SQLite (EF Core) |
| Десктопный клиент | C# / .NET 8 / Avalonia 11 (Windows, Linux/RED OS) |
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
- Создаются seed-пользователи (пароли — только для локальной разработки):
  - **Админ:** `admin@worktime.local` / `Admin@123`
  - **Сотрудник:** `employee@worktime.local` / `Employee@123`
  - **Руководитель:** `manager@worktime.local` / `Manager@123`

  А также справочные проекты и устройства для сотрудников.

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

Локальная база — `worktime-local.db` в папке запуска (путь настраивается).

### 5. Десктопный клиент (Avalonia)

```bash
cd WorkTime.Desktop
dotnet run
```

Кроссплатформенный GUI-клиент (Windows / Linux, включая RED OS): крупные кнопки
«Начать день / Перерыв / Вернуться / Закончить», выбор проекта, индикатор очереди
синхронизации. Работает офлайн: события пишутся в локальный SQLite и уходят на
сервер при появлении связи. Адрес сервера задаётся в окне входа (по умолчанию
`http://localhost:8080`).

UI собран кодом на C# без XAML, поэтому проект одинаково собирается на Windows и
Linux и не зависит от генераторов имён Avalonia.

---

## Конфигурация сервера

| Ключ | Файл | Назначение |
|------|------|-----------|
| `ConnectionStrings:WorkTimeDatabase` | appsettings.*.json | Строка подключения PostgreSQL |
| `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` | appsettings.json | Параметры JWT |
| `TimeEditing:EmployeeEditWindowDays` | appsettings.json | FR7: окно ручной правки прошлых периодов для Employee/Manager (по умолчанию 7 дней, Admin правит любые периоды) |

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

### Пользователи
| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/users` | Список пользователей (Manager+) |
| GET | `/api/users/{id}` | Пользователь по ID (Manager+) |
| POST | `/api/users` | Создание (Admin) |
| PUT | `/api/users/{id}` | Обновление (Admin) |
| DELETE | `/api/users/{id}` | Удаление (Admin) |
| POST | `/api/users/{id}/block` | Блокировка (Admin) |
| POST | `/api/users/{id}/unblock` | Разблокировка (Admin) |

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
| GET | `/api/reports/team/by-project` | Сводка по команде с группировкой по проектам (Manager+) |
| GET | `/api/reports/export/pdf` | Экспорт PDF (своих записей; Manager+ — любых) |
| GET | `/api/reports/export/xlsx` | Экспорт XLSX (своих записей; Manager+ — любых) |

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
├── WorkTime.Desktop/             # Десктопный клиент (Avalonia, UI на C# без XAML)
│   └── Services/                 # DesktopApiClient, DesktopPaths
├── WorkTime.ClientSdk/           # PWA SDK (IndexedDB, синхронизация)
├── WorkTime.WebReact/            # React PWA
│   └── src/
│       ├── components/           # Login, Dashboard, ManagerView, AdminView
│       ├── api.js                # API-клиент с JWT
│       └── pwaClient.js          # Мост к SDK
├── WorkTime.Server.Tests/        # Модульные тесты сервера
├── WorkTime.Server.IntegrationTests/  # Интеграционные тесты (WebApplicationFactory)
├── docker-compose.yml            # Docker: PostgreSQL + сервер
├── start-dev.ps1                 # Windows: локальный запуск всех сервисов
├── start-dev.sh                  # Linux/macOS: локальный запуск всех сервисов
└── .dockerignore                 # Игнор для docker build
```

## Тестирование

```bash
# Модульные + интеграционные тесты сервера (21 тест)
dotnet test WorkTimeSystem.slnx

# Сервер
dotnet build WorkTime.Server/WorkTime.Server.csproj

# Клиенты
dotnet build WorkTime.Client/WorkTime.Client.csproj
dotnet build WorkTime.Desktop/WorkTime.Desktop.csproj

# Веб-клиент
cd WorkTime.WebReact && npm install && npm run build
```

CI (`.github/workflows/ci.yml`) собирает всё решение на ubuntu-latest в Release и
прогоняет тесты против PostgreSQL-сервиса.