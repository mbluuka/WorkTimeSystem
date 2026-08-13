using WorkTime.Client.Data;
using WorkTime.Client.Models;
using WorkTime.Client.Services;
using WorkTime.Client.Storage;

var userId = Guid.Parse(
    "11111111-1111-1111-1111-111111111111");

var deviceId = Guid.Parse(
    "22222222-2222-2222-2222-222222222222");

await using var db = new LocalDbContext();

var store = new LocalEventStore(db);

await store.InitializeAsync();

var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5114/")
};

var syncService = new SyncService(store, httpClient);


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