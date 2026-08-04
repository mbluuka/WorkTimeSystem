using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.Models;

namespace WorkTime.Server.Services;

public class DbIdempotencyService : IIdempotencyService
{
    private readonly WorkTimeDbContext _dbContext;

    public DbIdempotencyService(WorkTimeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public bool TryGet(string key, out CachedResponse? response)
    {
        var entry = _dbContext.IdempotencyEntries.AsNoTracking().FirstOrDefault(e => e.Key == key);
        if (entry is null)
        {
            response = null;
            return false;
        }

        response = new CachedResponse(entry.StatusCode, entry.ContentType ?? "application/json", entry.Body ?? Array.Empty<byte>());
        return true;
    }

    public void Set(string key, CachedResponse response, TimeSpan? ttl = null)
    {
        var entry = new IdempotencyEntry
        {
            Key = key,
            StatusCode = response.StatusCode,
            ContentType = response.ContentType,
            Body = response.Body,
            CreatedAt = DateTime.UtcNow
        };

        // Upsert
        var existing = _dbContext.IdempotencyEntries.Find(key);
        if (existing is null)
        {
            _dbContext.IdempotencyEntries.Add(entry);
        }
        else
        {
            existing.StatusCode = entry.StatusCode;
            existing.ContentType = entry.ContentType;
            existing.Body = entry.Body;
            existing.CreatedAt = entry.CreatedAt;
            _dbContext.IdempotencyEntries.Update(existing);
        }

        _dbContext.SaveChanges();
    }
}
