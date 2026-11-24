# WorkTime.Mobile

Мобильный клиент (FR19) распределённой системы учёта рабочего времени.
Flutter + Dart, платформа — Android (minSdk 26 = Android 8.0, NFR5).

Работает по той же local-first модели, что десктопный (`WorkTime.Desktop`) и
консольный (`WorkTime.Client`) клиенты: отметки сначала пишутся в локальную
SQLite, затем уходят на сервер при наличии связи; локальные данные не теряются
при офлайне и переживают перезапуск приложения.

## Структура

```
lib/
├── main.dart                       # корневой виджет, выбор экрана по сессии
├── models/work_event.dart          # WorkEvent, WorkEventType, SyncStatus
├── storage/local_event_store.dart  # SQLite: очередь Pending, DeviceSeq, сводка
├── services/
│   ├── api_client.dart             # REST + JWT, разбор userId из токена
│   ├── device_identity.dart        # стабильный device id, сохранение сессии
│   └── sync_service.dart           # push/pull-синхронизация
└── screens/
    ├── login_screen.dart           # вход (FR11), адрес сервера настраивается
    └── home_screen.dart            # отметки дня, история, статус синхронизации
```

## Соответствие требованиям

| Требование | Реализация |
|-----------|-----------|
| FR1–FR4 (отметки начала/конца дня и перерывов) | кнопки на `HomeScreen`, типы `StartWork`/`StartBreak`/`EndBreak`/`EndWork` |
| FR5 (привязка к проекту) | выпадающий список проектов, `projectId` в записи |
| FR6 (история своих записей) | список последних записей + pull с сервера |
| FR7 (самостоятельная корректировка) | изменение времени и удаление; окно 7 дней проверяет сервер (`PeriodEditGuard`) |
| FR8 (сводка) | `summarize`: отработано / перерыв / остаток до 8 часов |
| FR9 (локальное хранение) | SQLite `worktime-local.db`, схема как у `LocalDbContext` |
| FR10 (синхронизация) | `SyncService.syncNow`: push очереди, pull, самолечение регистрации устройства |
| FR11 (авторизация) | JWT из `POST /api/auth/login`, токен сохраняется |
| NFR5 (Android 8+) | `minSdk = 26` |

## Сборка

Требуется Flutter SDK (3.50+) и Android SDK; JDK — из поставке Android Studio.

```powershell
$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
flutter pub get
flutter build apk --debug     # build\app\outputs\flutter-apk\app-debug.apk
```

Проверка кода и тесты:

```powershell
flutter analyze
flutter test
```

## Запуск против локального сервера

1. Поднять сервер: `docker compose up -d` (порт 8080) или `dotnet run --project WorkTime.Server`.
2. В эмуляторе Android хост доступен как `10.0.2.2`, поэтому адрес сервера —
   `http://10.0.2.2:8080` (значение по умолчанию на экране входа).
3. На физическом устройстве указать LAN-IP машины с сервером, например
   `http://192.168.0.10:8080`; сервер должен слушать не только localhost
   (`dotnet run --urls http://0.0.0.0:8080`).
4. Войти под учётной записью сотрудника (`employee@worktime.local` в сиде).

```powershell
flutter emulators --launch Medium_Phone_API_37.0
flutter run
```

## Ограничения

- **iOS** не собран: требуется macOS с Xcode. Код Android-специфичен только в
  `DeviceIdentity` (`dart:io Platform`), портирование сводится к `flutter create . --platforms=ios`.
- Правки и удаления, отклонённые сервером (запись старше 7 дней), остаются
  локальными: текст ошибки показывается, но запись не пропадает.
- Фоновой синхронизации по появлению сети нет — отправка выполняется при
  каждом действии, при pull-to-refresh и кнопкой синхронизации (как FR10
  реализовано и в других клиентах).
