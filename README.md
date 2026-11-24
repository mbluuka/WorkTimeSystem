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

## Публикация для RED OS (Linux x64)

Десктоп-клиент собирается из Windows и копируется на RED OS — устанавливать
.NET SDK на целевой машине **не нужно** (self-contained публикация включает
все зависимости, включая `libSkiaSharp.so`, `libe_sqlite3.so`, `libcoreclr.so`).

### Шаг 1. Требования на Windows

- .NET 8 SDK (любой источник, даже 11.0 — кросс-публикация работает из SDK 11)
- Командная строка (PowerShell / bash)

### Шаг 2. Публикация

```powershell
cd WorkTime.Desktop
dotnet publish -c Release -r linux-x64 --self-contained true -o ..\Desktop-RED-OS
```

Через 1-2 минуты в папке `Desktop-RED-OS` появится полный набор файлов (~100 МБ).

### Шаг 3. Копирование на RED OS

Скопируйте папку `Desktop-RED-OS` на RED OS любым способом (флешка, SCP,
Git-репозиторий). Пример через SCP:

```bash
# С Windows (PowerShell):
scp -r Desktop-RED-OS user@red-os-ip:/home/user/WorkTime-Desktop

# С Windows (WinSCP / FileZilla):
# скопировать папку Desktop-RED-OS в /home/user/WorkTime-Desktop
```

### Шаг 4. Настройка прав на RED OS

```bash
ssh user@red-os-ip
chmod +x WorkTime-Desktop/WorkTime.Desktop
```

### Шаг 5. Системные зависимости RED OS

На RED OS (как и на любом Linux) при первом запуске могут потребоваться
базовые X11/Wayland-библиотеки:

```bash
# Если пакетный менеджер `apt`:
sudo apt update
sudo apt install -y libx11-6 libxcomposite1 libxcursor1 libxdamage1 \
    libxfixes3 libxi6 libxrandr2 libxrender1 libxss1 libxtst6 \
    libasound2 libnss3 libdbus-1-3 libdrm2 libwayland-client0 libxkbcommon0

# Если используется `yum`/`dnf`:
sudo dnf install -y libX11 libXcomposite libXcursor libXdamage libXfixes \
    libXi libXrandr libXScrnSaver libXtst alsa-lib nss dbus-libs \
    libdrm wayland libxkbcommon
```

> **Примечание:** на RED OS 1.8+ (с GUI-окружением) эти библиотеки обычно уже
> присутствуют — если при запуске появляется окно с ошибкой «Не удалось
> загрузить libSkiaSharp.so», проверьте наличие `libX11` и `libglvnd`.

### Шаг 6. Запуск

```bash
cd /home/user/WorkTime-Desktop
./WorkTime.Desktop
```

При первом запуске откроется окно входа. Введите адрес сервера (по умолчанию
`http://localhost:8080`) и учётные данные.

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
4. **Разрешение конфликтов:** upsert по первичному ключу (`Id`) с сохранением
   порядка применения по `ServerOrder`. Сравнение временных меток (Last Write Wins),
   описанное в требованиях, на сервере **не реализовано** — см. «Соответствие требованиям»
5. **ServerOrder:** монотонная последовательность на сервере для упорядочивания событий

## Соответствие требованиям (FR/NFR)

Сверка по документу «Проектный практикум, 2-й семестр», таблицы 2–3.

### Функциональные требования

| ID | Требование | Статус | Где реализовано |
|----|-----------|--------|-----------------|
| FR1 | Фиксация начала рабочего дня | ✅ | `POST /api/workevents`, `WorkEventType.DayStart`; кнопки Dashboard/Desktop |
| FR2 | Фиксация окончания рабочего дня | ✅ | `WorkEventType.DayEnd` |
| FR3 | Фиксация начала перерыва | ✅ | `WorkEventType.BreakStart` |
| FR4 | Фиксация окончания перерыва | ✅ | `WorkEventType.BreakEnd` |
| FR5 | Выбор проекта/задачи | ✅ | `ProjectId` в `WorkEvent`, выбор проекта в Dashboard/Desktop |
| FR6 | История своих записей | ✅ | `GET /api/workevents`, `GET /api/timeentries`; таблица в Dashboard |
| FR7 | Ручная корректировка с ограничением по прошлым периодам | ✅ | `PeriodEditGuard`: окно `TimeEditing:EmployeeEditWindowDays` (7 дн.) для Employee/Manager, Admin без ограничений |
| FR8 | Автоподсчёт продолжительности и остатка дня | ✅ | `ReportSummary.RemainingMinutes` («до 8 часов»), таймер в Dashboard |
| FR9 | Локальная БД с полной офлайн-работой | ✅ | SQLite (Console/Desktop), IndexedDB (PWA), outbox-очередь |
| FR10 | **Фоновая** синхронизация при восстановлении связи | ⚠️ частично | Очередь `SyncQueue` + идемпотентность работают. PWA отправляет outbox **автоматически после каждого действия** (`Dashboard.jsx`, `syncOutbox` в `try/catch` — при офлайн остаётся в outbox). Но реактивации по событию появления сети **нет**: ни слушателя `online`, ни вызова `SyncManager.register()` для тега `sync-outbox`, который обрабатывается в service worker. Desktop и Console синхронизируются только по кнопке/команде. Итог: «при восстановлении соединения» не выполнено — надо снова выполнить действие или нажать кнопку |
| FR11 | Авторизация и аутентификация (JWT) | ✅ | `AuthController`, BCrypt, claims в JWT |
| FR12 | Ролевая модель Employee/Manager/Admin | ✅ | Проверки роли в контроллерах, `RoleBadge` в UI |
| FR13 | Просмотр записей команды руководителем | ✅ | `ManagerView.jsx`, `GET /api/users` доступен Manager |
| FR14 | Сводные отчёты (сотрудники/проекты/периоды) | ✅ | `GET /api/reports/team/by-project` |
| FR15 | Экспорт в PDF и XLSX | ✅ | QuestPDF + ClosedXML, эндпоинты `reports/export/*` |
| FR16 | CRUD пользователей администратором | ✅ | `UsersController` + `AdminView` (создание, редактирование ФИО/роли/блокировки, удаление) |
| FR17 | Просмотр аудита действий | ✅ | `AuditLogsController`, вкладка «Аудит» в AdminView |
| FR18 | Веб-интерфейс (React) | ✅ | `WorkTime.WebReact` (React 18 + Vite, PWA) |
| FR19 | **Мобильные приложения (Android/iOS)** | ❌ **не реализовано** | Flutter-клиент отсутствует, SDK не установлен. См. раздел «Мобильный клиент» ниже |
| FR20 | Десктоп (Windows/Linux/RED OS) | ✅ | `WorkTime.Desktop` (Avalonia 11, UI на C# без XAML), мануал публикации для RED OS |

### Нефункциональные требования

| ID | Требование | Статус | Комментарий |
|----|-----------|--------|-------------|
| NFR1 | Доступность 99,9 % | ➖ не измерялось | Нет мониторинга/алертинга; для учебного проекта не заявлено |
| NFR2 | До 50 одновременных пользователей | ➖ не измерялось | Нет нагрузочного тестирования; горизонтальное масштабирование не проверялось |
| NFR3 | Отклик API p95 ≤ 200 мс | ➕ выполнено на наблюдаемых данных | Ответы локального Docker-сервера — единицы–десятки мс; **формального замера p95 не было** |
| NFR4 | Синхронизация очереди до 50 записей ≤ 5 с | ➖ не измерялось | `BulkUpsertService` (COPY + upsert) спроектирован для пакетной загрузки, но время не замерено |
| NFR5 | Android 8+ | ❌ | Нет мобильного клиента (FR19) |
| NFR6 | Windows 10+, RED OS 7+ | ✅ | Собран и запущен на Windows; `linux-x64` self-contained опубликован (ELF проверен), на реальной RED OS не тестировалось |
| NFR7 | Офлайн неограниченно по времени | ✅ | Локальная БД + очередь без таймаута хранения |

### Расхождения с документом

1. **Разрешение конфликтов.** В разделе «Подход к решению проблемы» описан *Last Write Wins*
   со сравнением временных меток. Сервер реализует **upsert по первичному ключу `Id`**
   (`ON CONFLICT ("Id") DO UPDATE`) с сохранением порядка по `ServerOrder`: более поздняя
   по `ServerOrder` операция перезаписывает запись целиком, независимо от того, какая
   временная метка в данных больше. Повторная отправка той же операции не создаёт
   дубликатов, но «более свежая» правка с другого устройства победит только за счёт
   порядка приёма, а не сравнения меток.
2. **Поля `sync_version` / `last_modified` / `sync_status`** из проекта структуры БД
   частично не соответствуют реализации: `User.SyncVersion` присутствует в модели,
   но нигде не используется; `TimeEntry` не имеет `LastModified`/`SyncStatus`
   (используется `UpdatedAt`); enum `SyncStatus` в коде не применяется.
   Фактически роль версии выполняет `WorkEvent.DeviceSeq` + `ServerOrder`.
3. **Локальная БД мобильного клиента** по документу — Hive;Flutter-клиент не разработан,
   поэтому выбор хранилища не подтверждён практикой.
4. **Redis (опционально)** не используется: очереди и идемпотентность реализованы
   через PostgreSQL. Требование помечено как опциональное, отступление допустимо.

### Мобильный клиент (FR19) — что нужно для реализации

Требуется установить (ориентировочно 6–8 ГБ на диске):

| Компонент | Зачем | Вариант A (эмулятор) | Вариант B (реальный телефон) |
|-----------|-------|:---:|:---:|
| Flutter SDK (stable) | Компилятор Dart, `flutter` CLI, шаблоны проектов | обязателен | обязателен |
| Git | Нужен Flutter SDK для своей работы | обязателен | обязателен |
| Android SDK + platform-tools (`adb`) | Сборка APK, разрешение лицензий | обязателен | обязателен |
| Android Studio | GUI-установка SDK/образов, AVD Manager | рекомендуется | не обязателен (хватит `sdkmanager`) |
| JDK 17 | Gradle требует Java 17 | обязателен | обязателен |
| Образ AVD (x86_64, API 33+) + эмулятор | Виртуальное устройство для запуска | обязателен | не нужен |
| USB-отладка на телефоне + драйвер | Установка APK на устройство | не нужен | обязателен |
| iOS: macOS + Xcode | Сборка под iOS | невозможно на Windows | невозможно на Windows |

**Ограничение по iOS:** собрать IPA на Windows нельзя — требуется macOS с Xcode.
FR19 формулируется как «Android/iOS», поэтому реалистичный объём — **Android**,
а для iOS обосновать платформу-кандидат и оставить как направление развития.

**Проверка окружения после установки:**

```powershell
flutter --version
flutter doctor          # все критичные компоненты должны быть ✓
flutter doctor --android-licenses
```

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
# Тесты сервера: 25 модульных + 5 интеграционных = 30 тестов
dotnet test WorkTimeSystem.slnx

# Только модульные (без Docker)
dotnet test WorkTime.Server.Tests/WorkTime.Server.Tests.csproj

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

### Сквозная проверка (E2E)

Требует запущенного Docker Desktop и собранного образа:

```powershell
docker compose build server
docker compose up -d server
.\verify-e2e.ps1
```

Скрипт проверяет на живом сервере: FR7 (блокировка правки старых периодов),
FR13 (Manager читает список пользователей), FR14 (отчёт по команде с группировкой
по проектам), FR15 (экспорт PDF/XLSX), FR16 (установка и снятие блокировки через
`PUT /api/users/{id}`, запрет блокировки собственной учётной записи, запрет удаления
проекта с привязанными записями). Успешные проверки печатаются как `OK ...`,
ошибки — как `FAIL ...`.