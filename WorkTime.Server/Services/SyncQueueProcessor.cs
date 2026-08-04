using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
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

    public SyncQueueProcessor(IServiceProvider provider, ILogger<SyncQueueProcessor> logger, IOptions<SyncQueueOptions> options)
    {
        _provider = provider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncQueueProcessor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatch(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sync queue");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessBatch(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();
        var bulk = scope.ServiceProvider.GetRequiredService<IBulkUpsertService>();

        var now = DateTime.UtcNow;

        var entries = await db.SyncQueue
            .Where(e => e.ProcessedAt == null && (e.NextRetry == null || e.NextRetry <= now) && e.Attempts < _options.MaxAttempts)
            .OrderBy(e => e.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (!entries.Any()) return;

        foreach (var entry in entries)
        {
            try
            {
                entry.Attempts++;

                if (string.Equals(entry.Operation, "create", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Operation, "update", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(entry.Data))
                        throw new InvalidOperationException("Empty data for create/update operation");

                    if (string.Equals(entry.EntityType, "WorkEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        var ev = JsonSerializer.Deserialize<WorkEvent>(entry.Data!);
                        if (ev == null) throw new InvalidOperationException("Failed to deserialize WorkEvent");

                        await bulk.BulkUpsertAsync(new[] { ev }, ct);
                        entry.ProcessedAt = DateTime.UtcNow;
                        entry.Result = "Applied";
                    }
                    else if (string.Equals(entry.EntityType, "TimeEntry", StringComparison.OrdinalIgnoreCase))
                    {
                        var te = JsonSerializer.Deserialize<TimeEntry>(entry.Data!);
                        if (te == null) throw new InvalidOperationException("Failed to deserialize TimeEntry");

                        var existing = await db.TimeEntries.FindAsync(new object[] { te.Id }, ct);
                        if (existing == null)
                        {
                            te.CreatedAt = te.CreatedAt == default ? DateTime.UtcNow : te.CreatedAt;
                            db.TimeEntries.Add(te);
                        }
                        else
                        {
                            // apply update fields
                            existing.Start = te.Start;
                            existing.End = te.End;
                            existing.UpdatedAt = DateTime.UtcNow;
                            db.TimeEntries.Update(existing);
                        }

                        await db.SaveChangesAsync(ct);
                        entry.ProcessedAt = DateTime.UtcNow;
                        entry.Result = "Applied";
                    }
                    else
                    {
                        entry.Result = "UnsupportedEntity";
                        entry.ProcessedAt = DateTime.UtcNow;
                    }
                }
                else if (string.Equals(entry.Operation, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    var we = await db.WorkEvents.FindAsync(new object[] { entry.EntityId }, ct);
                    if (we != null)
                    {
                        db.WorkEvents.Remove(we);
                        await db.SaveChangesAsync(ct);
                    }

                    entry.ProcessedAt = DateTime.UtcNow;
                    entry.Result = "Deleted";
                }
                else
                {
                    entry.Result = "UnknownOperation";
                    entry.ProcessedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Processing entry {Id} failed (attempt {Attempt})", entry.Id, entry.Attempts);

                // Exponential backoff: base * 2^(attempts-1)
                var backoff = TimeSpan.FromSeconds(_options.BaseBackoffSeconds * Math.Pow(2, Math.Max(0, entry.Attempts - 1)));
                entry.NextRetry = DateTime.UtcNow.Add(backoff);
                entry.Result = ex.Message;
            }
            finally
            {
                // Persist attempt/nextRetry/result/processedAt
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to save SyncQueueEntry {Id}", entry.Id);
                }
            }
        }
    }
}
