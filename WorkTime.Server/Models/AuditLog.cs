namespace WorkTime.Server.Models;

public class AuditLog
{
    public Guid Id { get; set; }

    public Guid AdminId { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid? TargetUserId { get; set; }

    public string? TargetUserName { get; set; }

    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }

    public User? Admin { get; set; }
}
