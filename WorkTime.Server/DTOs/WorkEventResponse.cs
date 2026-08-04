using WorkTime.Server.Models;

namespace WorkTime.Server.DTOs;

public class WorkEventResponse
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid DeviceId { get; set; }

    public WorkEventType Type { get; set; }

    public DateTime Timestamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SyncedAt { get; set; }
    public DateTime? ServerReceivedAt { get; set; }
    public long? ServerOrder { get; set; }
}