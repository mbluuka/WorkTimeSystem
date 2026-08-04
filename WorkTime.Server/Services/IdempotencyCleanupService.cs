using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WorkTime.Server.Data;

namespace WorkTime.Server.Services;

public class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _delay;

    public IdempotencyCleanupService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _ttl = TimeSpan.FromDays(30); // default TTL
        _delay = TimeSpan.FromHours(24); // run daily
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();

                var threshold = DateTime.UtcNow - _ttl;
                var oldEntries = await db.IdempotencyEntries
                    .Where(e => e.CreatedAt < threshold)
                    .ToListAsync(stoppingToken);

                if (oldEntries.Any())
                {
                    db.IdempotencyEntries.RemoveRange(oldEntries);
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch
            {
                // swallow exceptions to avoid terminating the background service
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
