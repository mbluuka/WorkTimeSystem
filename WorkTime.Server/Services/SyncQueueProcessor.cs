using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkTime.Server.Data;
using WorkTime.Server.Models;

namespace WorkTime.Server.Services;

public class SyncQueueOptions
{
    public int IntervalSeconds { get; set; } = 10;

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 5;

    public int BaseBackoffSeconds { get; set; } = 30;
}

public class SyncQueueProcessor : BackgroundService
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<SyncQueueProcessor> _logger;
    private readonly SyncQueueOptions _options;

    public SyncQueueProcessor(
        IServiceProvider provider,
        ILogger<SyncQueueProcessor> logger,
        IOptions<SyncQueueOptions> options)
    {
        _provider = provider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncQueueProcessor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatch(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing sync queue");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.IntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SyncQueueProcessor stopped");
    }

    private async Task ProcessBatch(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<WorkTimeDbContext>();

        var bulk = scope.ServiceProvider
            .GetRequiredService<IBulkUpsertService>();

        var now = DateTime.UtcNow;

        var entries = await db.SyncQueue
            .Where(e =>
                e.ProcessedAt == null &&
                (e.NextRetry == null || e.NextRetry <= now) &&
                e.Attempts < _options.MaxAttempts)
            .OrderBy(e => e.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (!entries.Any())
        {
            return;
        }

        _logger.LogInformation(
            "Processing {Count} synchronization operations",
            entries.Count);

        foreach (var entry in entries)
        {
            try
            {
                entry.Attempts++;

                switch (entry.EntityType.ToLowerInvariant())
                {
                    case "workevent":
                        await ProcessWorkEvent(
                            db,
                            bulk,
                            entry,
                            ct);
                        break;

                    case "timeentry":
                        await ProcessTimeEntry(
                            db,
                            entry,
                            ct);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported entity type: {entry.EntityType}");
                }

                entry.ProcessedAt = DateTime.UtcNow;
                entry.NextRetry = null;

                if (string.IsNullOrWhiteSpace(entry.Result))
                {
                    entry.Result = "Applied";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Processing sync operation {Id} failed. Attempt {Attempt}",
                    entry.Id,
                    entry.Attempts);

                entry.Result = ex.Message;

                if (entry.Attempts >= _options.MaxAttempts)
                {
                    entry.ProcessedAt = DateTime.UtcNow;

                    _logger.LogError(
                        "Synchronization operation {Id} permanently failed after {Attempts} attempts",
                        entry.Id,
                        entry.Attempts);
                }
                else
                {
                    var backoffSeconds =
                        _options.BaseBackoffSeconds *
                        Math.Pow(
                            2,
                            Math.Max(0, entry.Attempts - 1));

                    entry.NextRetry =
                        DateTime.UtcNow.AddSeconds(backoffSeconds);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task ProcessWorkEvent(
        WorkTimeDbContext db,
        IBulkUpsertService bulk,
        SyncQueueEntry entry,
        CancellationToken ct)
    {
        if (entry.Operation.Equals(
                "create",
                StringComparison.OrdinalIgnoreCase) ||
            entry.Operation.Equals(
                "update",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry.Data))
            {
                throw new InvalidOperationException(
                    "WorkEvent data is empty");
            }

            var workEvent =
                JsonSerializer.Deserialize<WorkEvent>(
                    entry.Data);

            if (workEvent == null)
            {
                throw new InvalidOperationException(
                    "Failed to deserialize WorkEvent");
            }

            if (workEvent.Id != entry.EntityId)
            {
                throw new InvalidOperationException(
                    "WorkEvent Id does not match EntityId");
            }

            if (workEvent.UserId != entry.UserId)
            {
                throw new InvalidOperationException(
                    "WorkEvent UserId does not match operation UserId");
            }

            workEvent.ServerReceivedAt =
                DateTime.UtcNow;

            await bulk.BulkUpsertAsync(
                new[] { workEvent },
                ct);

            entry.Result = "Applied";
            return;
        }

        if (entry.Operation.Equals(
                "delete",
                StringComparison.OrdinalIgnoreCase))
        {
            var workEvent =
                await db.WorkEvents.FindAsync(
                    new object[] { entry.EntityId },
                    ct);

            if (workEvent != null)
            {
                db.WorkEvents.Remove(workEvent);
                await db.SaveChangesAsync(ct);
            }

            entry.Result = "Deleted";
            return;
        }

        throw new InvalidOperationException(
            $"Unknown WorkEvent operation: {entry.Operation}");
    }

    private static async Task ProcessTimeEntry(
        WorkTimeDbContext db,
        SyncQueueEntry entry,
        CancellationToken ct)
    {
        if (entry.Operation.Equals(
                "create",
                StringComparison.OrdinalIgnoreCase) ||
            entry.Operation.Equals(
                "update",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry.Data))
            {
                throw new InvalidOperationException(
                    "TimeEntry data is empty");
            }

            var timeEntry =
                JsonSerializer.Deserialize<TimeEntry>(
                    entry.Data);

            if (timeEntry == null)
            {
                throw new InvalidOperationException(
                    "Failed to deserialize TimeEntry");
            }

            if (timeEntry.Id != entry.EntityId)
            {
                throw new InvalidOperationException(
                    "TimeEntry Id does not match EntityId");
            }

            if (timeEntry.UserId != entry.UserId)
            {
                throw new InvalidOperationException(
                    "TimeEntry UserId does not match operation UserId");
            }

            var existing =
                await db.TimeEntries.FindAsync(
                    new object[] { timeEntry.Id },
                    ct);

            if (existing == null)
            {
                timeEntry.CreatedAt =
                    timeEntry.CreatedAt == default
                        ? DateTime.UtcNow
                        : timeEntry.CreatedAt;

                db.TimeEntries.Add(timeEntry);
            }
            else
            {
                existing.Start = timeEntry.Start;
                existing.End = timeEntry.End;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            entry.Result = "Applied";
            return;
        }

        if (entry.Operation.Equals(
                "delete",
                StringComparison.OrdinalIgnoreCase))
        {
            var timeEntry =
                await db.TimeEntries.FindAsync(
                    new object[] { entry.EntityId },
                    ct);

            if (timeEntry != null)
            {
                db.TimeEntries.Remove(timeEntry);
                await db.SaveChangesAsync(ct);
            }

            entry.Result = "Deleted";
            return;
        }

        throw new InvalidOperationException(
            $"Unknown TimeEntry operation: {entry.Operation}");
    }
}