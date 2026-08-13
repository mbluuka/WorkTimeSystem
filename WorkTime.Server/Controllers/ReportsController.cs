using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
        await SaveReportAsync(userId, from, to, "summary", summary);

        return Ok(summary);
    }

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

        IQueryable<User> usersQuery = currentRole == "Admin"
            ? _dbContext.Users.AsNoTracking()
            : _dbContext.Users.AsNoTracking().Where(u => u.Role == "Employee" || u.Role == "Manager");

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
                DaysWorked = userSummary.Days.Count
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

        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header()
                    .Text("WorkTime — Отчёт о рабочем времени")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text($"Пользователь: {user?.FullName ?? user?.Email ?? "Неизвестно"}").FontSize(14).Bold();
                        col.Item().Text($"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}").FontSize(12).FontColor(Colors.Grey.Medium);

                        col.Item().Text($"Часов отработано: {summary.TotalWorkHours}").FontSize(13);
                        col.Item().Text($"Минут перерывов: {summary.TotalBreakMinutes}").FontSize(13);
                        col.Item().Text($"Осталось до 8ч: {summary.RemainingMinutes} мин").FontSize(13);
                        col.Item().Text($"Дней отработано: {summary.Days.Count}").FontSize(13);

                        col.Item().PaddingTop(10).Text("Детализация по дням").FontSize(14).Bold();

                        if (summary.Days.Count == 0)
                        {
                            col.Item().Text("Нет данных за выбранный период").FontColor(Colors.Grey.Medium);
                        }
                        else
                        {
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("Дата").Bold().FontColor(Colors.White);
                                    header.Cell().Text("Начало").Bold().FontColor(Colors.White);
                                    header.Cell().Text("Конец").Bold().FontColor(Colors.White);
                                    header.Cell().Text("Работа (мин)").Bold().FontColor(Colors.White);
                                    header.Cell().Text("Перерыв (мин)").Bold().FontColor(Colors.White);

                                    header.Cell().Background(Colors.Blue.Medium).Padding(5);
                                    header.Cell().Background(Colors.Blue.Medium).Padding(5);
                                    header.Cell().Background(Colors.Blue.Medium).Padding(5);
                                    header.Cell().Background(Colors.Blue.Medium).Padding(5);
                                    header.Cell().Background(Colors.Blue.Medium).Padding(5);
                                });

                                foreach (var day in summary.Days)
                                {
                                    table.Cell().Text(day.Date.ToString("dd.MM.yyyy"));
                                    table.Cell().Text(day.StartWork?.ToString("HH:mm") ?? "—");
                                    table.Cell().Text(day.EndWork?.ToString("HH:mm") ?? "—");
                                    table.Cell().Text(day.WorkMinutes.ToString());
                                    table.Cell().Text(day.BreakMinutes.ToString());
                                }
                            });
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Страница ");
                        x.CurrentPageNumber();
                    });
            });
        });

        var pdfBytes = document.GeneratePdf();
        var fileName = $"report_{userId:yyyyMMddHHmmss}.pdf";

        return File(pdfBytes, "application/pdf", fileName);
    }

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

        var summary = BuildSummary(userId, events, from, to);
        var user = await _dbContext.Users.FindAsync(userId);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Отчёт");

        ws.Cell(1, 1).Value = "WorkTime — Отчёт о рабочем времени";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.Blue;
        ws.Range(1, 1, 1, 5).Merge();

        ws.Cell(2, 1).Value = "Пользователь:";
        ws.Cell(2, 2).Value = user?.FullName ?? user?.Email ?? "Неизвестно";
        ws.Cell(3, 1).Value = "Период:";
        ws.Cell(3, 2).Value = $"{from:dd.MM.yyyy} — {to:dd.MM.yyyy}";

        ws.Cell(5, 1).Value = "Показатель";
        ws.Cell(5, 2).Value = "Значение";
        ws.Range(5, 1, 5, 2).Style.Font.Bold = true;
        ws.Range(5, 1, 5, 2).Style.Fill.BackgroundColor = XLColor.Blue;
        ws.Range(5, 1, 5, 2).Style.Font.FontColor = XLColor.White;

        ws.Cell(6, 1).Value = "Часов отработано";
        ws.Cell(6, 2).Value = summary.TotalWorkHours;
        ws.Cell(7, 1).Value = "Минут перерывов";
        ws.Cell(7, 2).Value = summary.TotalBreakMinutes;
        ws.Cell(8, 1).Value = "Осталось до 8ч";
        ws.Cell(8, 2).Value = summary.RemainingMinutes;
        ws.Cell(9, 1).Value = "Дней отработано";
        ws.Cell(9, 2).Value = summary.Days.Count;

        ws.Cell(11, 1).Value = "Детализация по дням";
        ws.Cell(11, 1).Style.Font.Bold = true;
        ws.Cell(11, 1).Style.Font.FontSize = 14;

        ws.Cell(12, 1).Value = "Дата";
        ws.Cell(12, 2).Value = "Начало";
        ws.Cell(12, 3).Value = "Конец";
        ws.Cell(12, 4).Value = "Работа (мин)";
        ws.Cell(12, 5).Value = "Перерыв (мин)";
        ws.Range(12, 1, 12, 5).Style.Font.Bold = true;
        ws.Range(12, 1, 12, 5).Style.Fill.BackgroundColor = XLColor.Blue;
        ws.Range(12, 1, 12, 5).Style.Font.FontColor = XLColor.White;

        int row = 13;
        foreach (var day in summary.Days)
        {
            ws.Cell(row, 1).Value = day.Date.ToString("dd.MM.yyyy");
            ws.Cell(row, 2).Value = day.StartWork?.ToString("HH:mm") ?? "—";
            ws.Cell(row, 3).Value = day.EndWork?.ToString("HH:mm") ?? "—";
            ws.Cell(row, 4).Value = day.WorkMinutes;
            ws.Cell(row, 5).Value = day.BreakMinutes;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var fileName = $"report_{userId:yyyyMMddHHmmss}.xlsx";

        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
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
