namespace WorkTime.Server.Models;

public class Report
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public string ReportType { get; set; } = string.Empty; // "daily", "weekly", "monthly", "team"

    public string DataJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
