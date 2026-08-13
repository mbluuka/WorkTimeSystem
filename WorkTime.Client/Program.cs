using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WorkTime.Client.Data;
using WorkTime.Client.Models;
using WorkTime.Client.Services;
using WorkTime.Client.Storage;

// ============================================================
// WorkTime Console Client
// Local-first: все события сохраняются в SQLite, синхронизация
// выполняется при наличии соединения с сервером.
// ============================================================

var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5114/")
};

// --- Авторизация ---
var (userId, deviceId, token) = await AuthenticateAsync(httpClient);

if (userId == Guid.Empty)
{
    Console.WriteLine("Не удалось авторизоваться. Завершение.");
    return;
}

await using var db = new LocalDbContext();

var store = new LocalEventStore(db);

await store.InitializeAsync();

var syncService = new SyncService(store, httpClient, token);

Console.WriteLine();
Console.WriteLine($"Пользователь: {userId}");
Console.WriteLine($"Устройство:   {deviceId}");
Console.WriteLine();

while (true)
{
    Console.Clear();

    Console.WriteLine("1 — Начать работу");
    Console.WriteLine("2 — Начать перерыв");
    Console.WriteLine("3 — Закончить перерыв");
    Console.WriteLine("4 — Закончить работу");
    Console.WriteLine("5 — Показать локальные события");
    Console.WriteLine("6 — Синхронизировать события");
    Console.WriteLine("0 — Выход");
    Console.WriteLine();

    Console.Write("Выберите действие: ");

    var choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await CreateEventAsync(
                store,
                userId,
                deviceId,
                WorkEventType.StartWork);

            break;

        case "2":
            await CreateEventAsync(
                store,
                userId,
                deviceId,
                WorkEventType.StartBreak);

            break;

        case "3":
            await CreateEventAsync(
                store,
                userId,
                deviceId,
                WorkEventType.EndBreak);

            break;

        case "4":
            await CreateEventAsync(
                store,
                userId,
                deviceId,
                WorkEventType.EndWork);

            break;

        case "5":
            await ShowEventsAsync(store);

            break;

        case "6":
            await syncService.SyncPendingEventsAsync();
            Pause();
            break;

        case "0":
            return;

        default:
            Console.WriteLine();
            Console.WriteLine("Неизвестная команда.");
            Pause();

            break;
    }
}

// ============================================================
// Авторизация: логин или регистрация
// ============================================================
static async Task<(Guid UserId, Guid DeviceId, string Token)> AuthenticateAsync(HttpClient httpClient)
{
    Console.WriteLine("=== WorkTime Console Client ===");
    Console.WriteLine();

    Console.Write("Email: ");
    var email = Console.ReadLine()?.Trim() ?? string.Empty;

    Console.Write("Пароль: ");
    var password = ReadPassword();

    Console.WriteLine();
    Console.WriteLine("Вход в систему...");

    try
    {
        var loginResponse = await httpClient.PostAsJsonAsync(
            "api/auth/login",
            new { email, password });

        if (!loginResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Ошибка входа: HTTP {(int)loginResponse.StatusCode}");
            return (Guid.Empty, Guid.Empty, string.Empty);
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        if (auth is null || string.IsNullOrWhiteSpace(auth.Token))
        {
            Console.WriteLine("Пустой ответ сервера.");
            return (Guid.Empty, Guid.Empty, string.Empty);
        }

        // Декодируем JWT, чтобы получить userId
        var userId = DecodeUserId(auth.Token);

        if (userId == Guid.Empty)
        {
            Console.WriteLine("Не удалось извлечь userId из токена.");
            return (Guid.Empty, Guid.Empty, string.Empty);
        }

        // Регистрируем устройство (или используем сохранённое)
        var deviceId = await RegisterDeviceAsync(httpClient, auth.Token, userId);

        return (userId, deviceId, auth.Token);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка сети: {ex.Message}");
        return (Guid.Empty, Guid.Empty, string.Empty);
    }
}

static async Task<Guid> RegisterDeviceAsync(HttpClient httpClient, string token, Guid userId)
{
    // Пытаемся найти устройство на сервере
    httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);

    try
    {
        var devices = await httpClient.GetFromJsonAsync<List<DeviceDto>>("api/devices");

        if (devices is not null && devices.Any(d => d.UserId == userId))
        {
            return devices.First(d => d.UserId == userId).Id;
        }
    }
    catch
    {
        // Устройств нет или endpoint не существует — создаём новое
    }

    // Создаём новое устройство
    var platform = OperatingSystem.IsWindows() ? "Windows" : "Linux";
    var name = $"Console Client ({Environment.MachineName})";

    try
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/devices",
            new { userId, name, platform });

        if (response.IsSuccessStatusCode)
        {
            var device = await response.Content.ReadFromJsonAsync<DeviceDto>();
            if (device is not null)
            {
                return device.Id;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Не удалось зарегистрировать устройство: {ex.Message}");
    }

    // Fallback: генерируем локальный ID устройства
    return Guid.NewGuid();
}

static Guid DecodeUserId(string token)
{
    try
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return Guid.Empty;

        var payload = parts[1];
        var json = Base64UrlDecode(payload);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("nameid", out var nameId))
        {
            return Guid.TryParse(nameId.GetString(), out var id) ? id : Guid.Empty;
        }

        if (root.TryGetProperty("sub", out var sub))
        {
            return Guid.TryParse(sub.GetString(), out var id) ? id : Guid.Empty;
        }
    }
    catch
    {
        // ignore
    }

    return Guid.Empty;
}

static string Base64UrlDecode(string input)
{
    var padded = input.Replace('-', '+').Replace('_', '/');
    switch (padded.Length % 4)
    {
        case 2: padded += "=="; break;
        case 3: padded += "="; break;
    }

    var bytes = Convert.FromBase64String(padded);
    return System.Text.Encoding.UTF8.GetString(bytes);
}

static string ReadPassword()
{
    var password = string.Empty;
    ConsoleKey key;

    do
    {
        var keyInfo = Console.ReadKey(intercept: true);
        key = keyInfo.Key;

        if (key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password[..^1];
            Console.Write("\b \b");
        }
        else if (key != ConsoleKey.Enter && key != ConsoleKey.Backspace)
        {
            password += keyInfo.KeyChar;
            Console.Write("*");
        }
    } while (key != ConsoleKey.Enter);

    return password;
}

static async Task CreateEventAsync(
    LocalEventStore store,
    Guid userId,
    Guid deviceId,
    WorkEventType type)
{
    var now = DateTime.UtcNow;

    var workEvent = new LocalWorkEvent
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceId = deviceId,
        Type = type,
        Timestamp = now,
        CreatedAt = now,
        SyncStatus = SyncStatus.Pending
    };

    await store.AddAsync(workEvent);

    Console.WriteLine();
    Console.WriteLine("Событие сохранено локально.");
    Console.WriteLine();
    Console.WriteLine($"Тип:       {workEvent.Type}");
    Console.WriteLine($"Время:     {workEvent.Timestamp:u}");
    Console.WriteLine($"Статус:    {workEvent.SyncStatus}");
    Console.WriteLine($"ID:        {workEvent.Id}");

    Pause();
}

static async Task ShowEventsAsync(LocalEventStore store)
{
    var events = await store.GetAllAsync();

    Console.WriteLine();
    Console.WriteLine("Локальные события:");
    Console.WriteLine();

    if (events.Count == 0)
    {
        Console.WriteLine("Событий пока нет.");
        Pause();
        return;
    }

    foreach (var workEvent in events)
    {
        Console.WriteLine(
            $"{workEvent.Timestamp:u} | " +
            $"{workEvent.Type,-10} | " +
            $"{workEvent.SyncStatus,-8} | " +
            $"{workEvent.Id}");
    }

    Console.WriteLine();
    Console.WriteLine($"Всего событий: {events.Count}");

    Pause();
}

static void Pause()
{
    Console.WriteLine();
    Console.WriteLine("Нажмите Enter для продолжения...");
    Console.ReadLine();
}

// ============================================================
// DTOs
// ============================================================
record AuthResponse(string Token, string Role);

record DeviceDto(Guid Id, Guid UserId, string Name, string Platform);