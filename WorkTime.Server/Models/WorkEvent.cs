namespace WorkTime.Server.Models;

public class WorkEvent
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid DeviceId { get; set; }

    public WorkEventType Type { get; set; }

    public DateTime Timestamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SyncedAt { get; set; }

    // Monotonic sequence generated on device to preserve local order
    public long DeviceSeq { get; set; }

    // When server received the event
    public DateTime? ServerReceivedAt { get; set; }

    // Global server ordering (sequence) assigned by DB
    public long? ServerOrder { get; set; }

    public User? User { get; set; }

    public Device? Device { get; set; }
}