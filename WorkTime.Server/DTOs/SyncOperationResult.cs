namespace WorkTime.Server.DTOs;

public class SyncOperationResult
{
    public Guid Id { get; set; }

    public string Status { get; set; } = "Pending";

    public string? Message { get; set; }
}