using System.Net.Http.Json;
using WorkTime.Client.Models;
using WorkTime.Client.Storage;

namespace WorkTime.Client.Servvices;

public class SyncService
{
    private readonly LocalEventStore _store;
    private readonly HttpClient _httpClient;

    public SyncService(
        LocalEventStore store,
        HttpClient httpClient)
    {
        _store = store;
        _httpClient = httpClient;
    }

    public async Task SyncPendingEventsAsync()
    {
        var pendingEvents = await _store.GetPendingAsync();

        Console.WriteLine();
        Console.WriteLine(
            $"Найдено событий для синхронизации: {pendingEvents.Count}");

        foreach (var workEvent in pendingEvents)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    "api/WorkEvents",
                    new
                    {
                        id = workEvent.Id,
                        userId = workEvent.UserId,
                        deviceId = workEvent.DeviceId,
                        type = workEvent.Type,
                        timestamp = workEvent.Timestamp,
                        createdAt = workEvent.CreatedAt
                    });

                if (response.IsSuccessStatusCode)
                {
                    workEvent.SyncStatus = SyncStatus.Synced;
                    workEvent.SyncedAt = DateTime.UtcNow;

                    await _store.UpdateAsync(workEvent);

                    Console.WriteLine(
                        $"✓ {workEvent.Type} " +
                        $"({workEvent.Id}) синхронизировано.");
                }
                else
                {
                    Console.WriteLine(
                        $"✗ {workEvent.Type} " +
                        $"({workEvent.Id}) — " +
                        $"HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"✗ Ошибка синхронизации " +
                    $"{workEvent.Id}: {ex.Message}");
            }
        }
    }
}