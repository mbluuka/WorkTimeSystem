namespace WorkTime.Server.DTOs;

public class TimeEntryResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public TimeSpan? Duration { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string EntryType { get; set; } = "work";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ReportSummaryRequest
{
    public Guid UserId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string? Period { get; set; } // daily, weekly, monthly
}

public class TeamReportRequest
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Period { get; set; }
}

public class ReportSummaryResponse
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public long TotalWorkMinutes { get; set; }
    public long TotalBreakMinutes { get; set; }
    public long TotalWorkHours => TotalWorkMinutes / 60;
    public int RemainingMinutes => (int)Math.Max(0, 480 - TotalWorkMinutes); // 8 hours default
    public List<DaySummary> Days { get; set; } = new();
}

public class DaySummary
{
    public DateTime Date { get; set; }
    public long WorkMinutes { get; set; }
    public long BreakMinutes { get; set; }
    public DateTime? StartWork { get; set; }
    public DateTime? EndWork { get; set; }
    public string? ProjectName { get; set; }
}

public class TeamReportResponse
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<EmployeeSummary> Employees { get; set; } = new();
    public long TotalTeamMinutes { get; set; }
}

public class EmployeeSummary
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public long TotalWorkMinutes { get; set; }
    public long TotalBreakMinutes { get; set; }
    public int DaysWorked { get; set; }
    public string? ProjectName { get; set; }
}
