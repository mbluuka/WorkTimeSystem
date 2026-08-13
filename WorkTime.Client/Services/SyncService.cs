using System.Net.Http.Headers;
using System.Net.Http.Json;
using WorkTime.Client.Models;
using WorkTime.Client.Storage;

namespace WorkTime.Client.Services;

public class SyncService
{
    private readonly LocalEventStore _store;
    private readonly HttpClient _httpClient;
    private readonly string? _token;

    public SyncService(
        LocalEventStore store,
        HttpClient httpClient,
        string? token = null)
    {
        _store = store;
        _httpClient = httpClient;
        _token = token;

        if (!string.IsNullOrWhiteSpace(_token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
        }
    }

    public async Task SyncPendingEventsAsync()
    {
        var pendingEvents = await _store.GetPendingAsync();

        Console.WriteLine();
        Console.WriteLine(
            $"Найдено событий для синхронизации: {pendingEvents.Count}");

        if (pendingEvents.Count == 0)
        {
            Console.WriteLine("Нет событий для отправки.");
            return;
        }

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
                    var errorText = await response.Content.ReadAsStringAsync();
                    Console.WriteLine(
                        $"✗ {workEvent.Type} " +
                        $"({workEvent.Id}) — " +
                        $"HTTP {(int)response.StatusCode}: {errorText}");
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