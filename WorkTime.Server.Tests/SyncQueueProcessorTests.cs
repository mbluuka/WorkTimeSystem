using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using WorkTime.Server.Data;
using WorkTime.Server.Models;
using WorkTime.Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Threading;

public class SyncQueueProcessorTests
{
    private class TestBulkUpsertService : IBulkUpsertService
    {
        public static int Calls;
        public static bool ThrowOnce;

        public Task BulkUpsertAsync(IEnumerable<WorkEvent> events, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnce && Calls == 1) throw new System.Exception("transient");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Processor_Applies_WorkEvent_Success()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WorkTimeDbContext>(o => o.UseInMemoryDatabase("db_success"));
        services.AddScoped<IBulkUpsertService, TestBulkUpsertService>();
        services.Configure<SyncQueueOptions>(o => { o.IntervalSeconds = 1; o.BatchSize = 10; o.BaseBackoffSeconds = 1; o.MaxAttempts = 3; });
        var provider = services.BuildServiceProvider();

        // seed entry
        var ev = new WorkEvent { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), Type = WorkEventType.StartWork, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();
            var entry = new SyncQueueEntry
            {
                Id = Guid.NewGuid(),
                UserId = ev.UserId,
                EntityType = "WorkEvent",
                EntityId = ev.Id,
                Operation = "create",
                Data = JsonSerializer.Serialize(ev),
                Version = 1,
                Attempts = 0,
                CreatedAt = DateTime.UtcNow
            };
            db.SyncQueue.Add(entry);
            await db.SaveChangesAsync();
        }

        var options = provider.GetRequiredService<IOptions<SyncQueueOptions>>();
        var processor = new SyncQueueProcessor(provider, new NullLogger<SyncQueueProcessor>(), options);

        using var cts = new CancellationTokenSource();
        var start = processor.StartAsync(cts.Token);

        var processed = false;
        for (int i = 0; i < 20; i++)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();
            var q = await db.SyncQueue.FirstOrDefaultAsync(x => x.EntityId == ev.Id);
            if (q != null && q.ProcessedAt != null)
            {
                processed = true;
                Assert.Equal("Applied", q.Result);
                Assert.Equal(1, q.Attempts);
                break;
            }

            await Task.Delay(200);
        }

        cts.Cancel();
        await processor.StopAsync(CancellationToken.None);

        Assert.True(processed, "Entry should be processed");
        Assert.True(TestBulkUpsertService.Calls >= 1);
    }

    [Fact]
    public async Task Processor_Retries_On_Transient_Failure_And_Succeeds()
    {
        TestBulkUpsertService.Calls = 0;
        TestBulkUpsertService.ThrowOnce = true;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WorkTimeDbContext>(o => o.UseInMemoryDatabase("db_retry"));
        services.AddScoped<IBulkUpsertService, TestBulkUpsertService>();
        services.Configure<SyncQueueOptions>(o => { o.IntervalSeconds = 1; o.BatchSize = 10; o.BaseBackoffSeconds = 1; o.MaxAttempts = 5; });
        var provider = services.BuildServiceProvider();

        // seed entry
        var ev = new WorkEvent { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), Type = WorkEventType.StartWork, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();
            var entry = new SyncQueueEntry
            {
                Id = Guid.NewGuid(),
                UserId = ev.UserId,
                EntityType = "WorkEvent",
                EntityId = ev.Id,
                Operation = "create",
                Data = JsonSerializer.Serialize(ev),
                Version = 1,
                Attempts = 0,
                CreatedAt = DateTime.UtcNow
            };
            db.SyncQueue.Add(entry);
            await db.SaveChangesAsync();
        }

        var options = provider.GetRequiredService<IOptions<SyncQueueOptions>>();
        var processor = new SyncQueueProcessor(provider, new NullLogger<SyncQueueProcessor>(), options);

        using var cts = new CancellationTokenSource();
        var start = processor.StartAsync(cts.Token);

        var processed = false;
        for (int i = 0; i < 50; i++)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();
            var q = await db.SyncQueue.FirstOrDefaultAsync(x => x.EntityId == ev.Id);
            if (q != null && q.ProcessedAt != null)
            {
                processed = true;
                Assert.Equal("Applied", q.Result);
                Assert.True(q.Attempts >= 2);
                break;
            }

            await Task.Delay(200);
        }

        cts.Cancel();
        await processor.StopAsync(CancellationToken.None);

        Assert.True(processed, "Entry should be processed after retry");
        Assert.True(TestBulkUpsertService.Calls >= 2);
    }
}
