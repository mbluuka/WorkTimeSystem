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

		// EnsureCreated не трогает существующую БД, а DeviceSeq/ProjectId
		// появились позже — добавляем недостающие колонки вручную.
		await EnsureColumnAsync("DeviceSeq", "INTEGER NOT NULL DEFAULT 0");
		await EnsureColumnAsync("ProjectId", "TEXT NULL");
	}

	public async Task<long> NextDeviceSeqAsync()
	{
		var max = await _db.WorkEvents
			.Select(x => (long?)x.DeviceSeq)
			.MaxAsync();

		return (max ?? 0) + 1;
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

	private async Task EnsureColumnAsync(string columnName, string columnDefinition)
	{
		var connection = _db.Database.GetDbConnection();

		if (connection.State != System.Data.ConnectionState.Open)
		{
			await connection.OpenAsync();
		}

		await using var check = connection.CreateCommand();
		check.CommandText = $"PRAGMA table_info(WorkEvents);";

		var existing = new List<string>();
		await using (var reader = await check.ExecuteReaderAsync())
		{
			while (await reader.ReadAsync())
			{
				// PRAGMA table_info: 1-я колонка — имя поля
				existing.Add(reader.GetString(1));
			}
		}

		if (existing.Contains(columnName, StringComparer.OrdinalIgnoreCase))
		{
			return;
		}

		await using var alter = connection.CreateCommand();
		alter.CommandText =
			$"ALTER TABLE WorkEvents ADD COLUMN {columnName} {columnDefinition};";

		await alter.ExecuteNonQueryAsync();
	}
}
