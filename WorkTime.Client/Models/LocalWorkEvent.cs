namespace WorkTime.Client.Models;

public enum WorkEventType
{
	StartWork = 0,
	StartBreak = 1,
	EndBreak = 2,
	EndWork = 3
}

public enum SyncStatus
{
	Pending = 0,
	Synced = 1
}

public class LocalWorkEvent
{
	public Guid Id { get; set; }

	public Guid UserId { get; set; }

	public Guid DeviceId { get; set; }

	public WorkEventType Type { get; set; }

	public DateTime Timestamp { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime? SyncedAt { get; set; }

	public SyncStatus SyncStatus { get; set; }
}