namespace WorkTime.Server.DTOs;

public enum SyncResultStatus
{
    Accepted,
    Upserted,
    Ignored,
    Invalid,
    Error
}

public class SyncResult
{
    public Guid Id { get; set; }

    public SyncResultStatus Status { get; set; }

    public string? Message { get; set; }
}
