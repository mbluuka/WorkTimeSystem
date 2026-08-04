using Microsoft.EntityFrameworkCore;
using WorkTime.Client.Data;
using WorkTime.Client.Models;

namespace WorkTime.Client.Storage;

public class LocalEventStore
{
	private readonly LocalDbContext _db;

	public LocalEventStore(LocalDbContext db)
	{
		_db = db;
	}

	public async Task InitializeAsync()
	{
		await _db.Database.EnsureCreatedAsync();
	}

	public async Task AddAsync(LocalWorkEvent workEvent)
	{
		var exists = await _db.WorkEvents
			.AnyAsync(x => x.Id == workEvent.Id);

		if (exists)
		{
			return;
		}

		_db.WorkEvents.Add(workEvent);

		await _db.SaveChangesAsync();
	}

	public async Task<List<LocalWorkEvent>> GetPendingAsync()
	{
		return await _db.WorkEvents
			.Where(x => x.SyncStatus == SyncStatus.Pending)
			.OrderBy(x => x.Timestamp)
			.ToListAsync();
	}

    public async Task UpdateAsync(LocalWorkEvent workEvent)
    {
        _db.WorkEvents.Update(workEvent);
        await _db.SaveChangesAsync();
    }

    public async Task MarkAsSyncedAsync(
		Guid eventId,
		DateTime syncedAt)
	{
		var workEvent = await _db.WorkEvents
			.FirstOrDefaultAsync(x => x.Id == eventId);

		if (workEvent is null)
		{
			return;
		}

		workEvent.SyncStatus = SyncStatus.Synced;
		workEvent.SyncedAt = syncedAt;

		await _db.SaveChangesAsync();
	}

	public async Task<List<LocalWorkEvent>> GetAllAsync()
	{
		return await _db.WorkEvents
			.OrderBy(x => x.Timestamp)
			.ToListAsync();
	}
}