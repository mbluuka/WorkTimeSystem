using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.DTOs;
using WorkTime.Server.Models;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(WorkTimeDbContext dbContext, ILogger<ReportsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Сводка по пользователю за период.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryResponse>> GetSummary(
        [FromQuery] Guid userId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        if (!CanAccessData(currentUserId, currentRole, userId))
            return Forbid();

        var events = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.Timestamp >= from && w.Timestamp <= to)
            .OrderBy(w => w.Timestamp)
            .ToListAsync();

        var summary = BuildSummary(userId, events, from, to);

        // Save cached report
        await SaveReportAsync(userId, from, to, "summary", summary);

        return Ok(summary);
    }

    /// <summary>
    /// Сводка по команде (Manager/Admin).
    /// </summary>
    [HttpGet("team")]
    public async Task<ActionResult<TeamReportResponse>> GetTeamReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? projectId = null,
        [FromQuery] string? period = null)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        if (currentRole != "Admin" && currentRole != "Manager")
            return Forbid();

        // Get all users the manager can see
        IQueryable<User> usersQuery;
        if (currentRole == "Admin")
        {
            usersQuery = _dbContext.Users.AsNoTracking();
        }
        else
        {
            // Manager sees only their own team - for now all non-admin users
            usersQuery = _dbContext.Users.AsNoTracking()
                .Where(u => u.Role == "Employee" || u.Role == "Manager");
        }

        var users = await usersQuery.ToListAsync();
        var teamSummaries = new List<EmployeeSummary>();
        long totalTeamMinutes = 0;

        foreach (var user in users)
        {
            var events = await _dbContext.WorkEvents
                .AsNoTracking()
                .Where(w => w.UserId == user.Id && w.Timestamp >= from && w.Timestamp <= to)
                .OrderBy(w => w.Timestamp)
                .ToListAsync();

            var userSummary = BuildSummary(user.Id, events, from, to);

            teamSummaries.Add(new EmployeeSummary
            {
                UserId = user.Id,
                UserName = user.Email,
                TotalWorkMinutes = userSummary.TotalWorkMinutes,
                TotalBreakMinutes = userSummary.TotalBreakMinutes,
                DaysWorked = userSummary.Days.Count,
                ProjectName = null
            });

            totalTeamMinutes += userSummary.TotalWorkMinutes;
        }

        var response = new TeamReportResponse
        {
            From = from,
            To = to,
            Employees = teamSummaries,
            TotalTeamMinutes = totalTeamMinutes
        };

        await SaveReportAsync(currentUserId, from, to, "team", response);

        return Ok(response);
    }

    /// <summary>
    /// Экспорт отчёта в PDF (заглушка — возвращает JSON для MVP).
    /// </summary>
    [HttpGet("export/pdf")]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] Guid userId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        if (!CanAccessData(currentUserId, currentRole, userId))
            return Forbid();

        var events = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.Timestamp >= from && w.Timestamp <= to)
            .OrderBy(w => w.Timestamp)
            .ToListAsync();

        var summary = BuildSummary(userId, events, from, to);
        var user = await _dbContext.Users.FindAsync(userId);

        // Для MVP возвращаем JSON. В продакшене заменить на QuestPDF
        var pdfData = new
        {
            title = "Отчёт о рабочем времени",
            user = user?.FullName ?? user?.Email,
            period = $"{from:dd.MM.yyyy} — {to:dd.MM.yyyy}",
            totalWorkHours = summary.TotalWorkHours,
            totalBreakMinutes = summary.TotalBreakMinutes,
            days = summary.Days
        };

        // В продакшене:
        // var pdfDocument = PDFGenerator.Generate(summary, user);
        // return File(pdfDocument.ToArray(), "application/pdf", $"report_{userId:yyyyMMdd}.pdf");

        return Ok(pdfData);
    }

    /// <summary>
    /// Экспорт отчёта в XLSX (заглушка).
    /// </summary>
    [HttpGet("export/xlsx")]
    public async Task<IActionResult> ExportXlsx(
        [FromQuery] Guid userId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        if (!CanAccessData(currentUserId, currentRole, userId))
            return Forbid();

        var events = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.Timestamp >= from && w.Timestamp <= to)
            .OrderBy(w => w.Timestamp)
            .ToListAsync();

        // В продакшене: ClosedXML
        // var workbook = new XLWorkbook();
        // var worksheet = workbook.Worksheets.Add("Report");
        // ...
        // return File(workbook.SaveAsBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"report_{userId:yyyyMMdd}.xlsx");

        return Ok(new { message = "XLSX export — implement ClosedXML for production" });
    }

    // --- Helpers ---

    private bool TryGetCurrentUser(out Guid userId, out string role)
    {
        userId = Guid.Empty;
        role = "Employee";

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out userId))
            return false;

        role = User.FindFirstValue(ClaimTypes.Role) ?? "Employee";
        return true;
    }

    private static bool CanAccessData(Guid currentUserId, string currentRole, Guid targetUserId)
    {
        return currentRole == "Admin" || currentRole == "Manager" || currentUserId == targetUserId;
    }

    private ReportSummaryResponse BuildSummary(Guid userId, List<WorkEvent> events, DateTime from, DateTime to)
    {
        var days = new List<DaySummary>();
        long totalWorkMinutes = 0;
        long totalBreakMinutes = 0;

        // Group by date
        var groupedByDate = events.GroupBy(w => w.Timestamp.Date).ToList();

        foreach (var dateGroup in groupedByDate)
        {
            var daySummary = new DaySummary { Date = dateGroup.Key };
            WorkEvent? startWork = null;
            WorkEvent? endWork = null;
            WorkEvent? startBreak = null;
            WorkEvent? endBreak = null;

            foreach (var ev in dateGroup.OrderBy(e => e.Timestamp))
            {
                switch (ev.Type)
                {
                    case WorkEventType.StartWork:
                        if (startWork == null) startWork = ev;
                        break;
                    case WorkEventType.EndWork:
                        endWork = ev;
                        break;
                    case WorkEventType.StartBreak:
                        startBreak = ev;
                        break;
                    case WorkEventType.EndBreak:
                        endBreak = ev;
                        break;
                }
            }

            daySummary.StartWork = startWork?.Timestamp;
            daySummary.EndWork = endWork?.Timestamp;

            if (startWork != null && endWork != null)
            {
                var workMinutes = (long)(endWork.Timestamp - startWork.Timestamp).TotalMinutes;
                daySummary.WorkMinutes = workMinutes;
                totalWorkMinutes += workMinutes;
            }

            if (startBreak != null && endBreak != null)
            {
                var breakMinutes = (long)(endBreak.Timestamp - startBreak.Timestamp).TotalMinutes;
                daySummary.BreakMinutes = breakMinutes;
                totalBreakMinutes += breakMinutes;
            }

            days.Add(daySummary);
        }

        return new ReportSummaryResponse
        {
            UserId = userId,
            From = from,
            To = to,
            TotalWorkMinutes = totalWorkMinutes,
            TotalBreakMinutes = totalBreakMinutes,
            Days = days
        };
    }

    private async Task SaveReportAsync(Guid userId, DateTime from, DateTime to, string type, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);

            var existing = await _dbContext.Reports
                .FirstOrDefaultAsync(r => r.UserId == userId &&
                    r.PeriodStart.Date == from.Date &&
                    r.PeriodEnd.Date == to.Date &&
                    r.ReportType == type);

            if (existing != null)
            {
                existing.DataJson = json;
                _dbContext.Reports.Update(existing);
            }
            else
            {
                var report = new Report
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PeriodStart = from,
                    PeriodEnd = to,
                    ReportType = type,
                    DataJson = json,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.Reports.Add(report);
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save report for user {UserId}", userId);
        }
    }
}
