using System.ComponentModel.DataAnnotations;

namespace WorkTime.Server.Models;

public class SyncQueueEntry
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string EntityType { get; set; } = string.Empty; // e.g., WorkEvent, TimeEntry

    public Guid EntityId { get; set; }

    public string Operation { get; set; } = string.Empty; // create/update/delete

    public string? Data { get; set; } // JSON payload

    public long Version { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextRetry { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? Result { get; set; }
}
