using System.Security.Claims;
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
public class TimeEntriesController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly ILogger<TimeEntriesController> _logger;

    public TimeEntriesController(WorkTimeDbContext dbContext, ILogger<TimeEntriesController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Получить временные записи пользователя (автоматически из WorkEvents).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimeEntryResponse>>> Get(
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid? projectId = null)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        // Employee can only see own entries
        var targetUserId = (currentRole == "Admin" || currentRole == "Manager")
            ? userId ?? currentUserId
            : currentUserId;

        var query = _dbContext.WorkEvents
            .AsNoTracking()
            .Where(w => w.UserId == targetUserId);

        if (from.HasValue)
            query = query.Where(w => w.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(w => w.Timestamp <= to.Value);

        var events = await query
            .OrderBy(w => w.Timestamp)
            .ToListAsync();

        // Group events into time entries
        var entries = GroupEventsIntoEntries(events);

        // Load project names if needed
        if (projectId.HasValue)
        {
            entries = entries.Where(e => e.ProjectId == projectId.Value).ToList();
        }

        return Ok(entries);
    }

    /// <summary>
    /// Получить одну временную запись.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TimeEntryResponse>> GetById(Guid id)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var entry = await GetTimeEntryById(id);
        if (entry is null)
            return NotFound();

        if (!CanAccessData(currentUserId, currentRole, entry.UserId))
            return Forbid();

        return Ok(entry);
    }

    /// <summary>
    /// Создать/обновить временную запись вручную.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TimeEntryResponse>> Create([FromBody] CreateTimeEntryRequest req)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        if (currentRole != "Admin" && currentRole != "Manager" && req.UserId != currentUserId)
            return Forbid();

        var entry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            UserId = req.UserId,
            Start = req.Start,
            End = req.End,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.TimeEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entry.Id }, ToResponse(entry, null));
    }

    /// <summary>
    /// Обновить временную запись.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TimeEntryResponse>> Update(Guid id, [FromBody] UpdateTimeEntryRequest req)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var entry = await _dbContext.TimeEntries.FindAsync(id);
        if (entry is null)
            return NotFound();

        if (!CanAccessData(currentUserId, currentRole, entry.UserId))
            return Forbid();

        if (req.Start.HasValue)
            entry.Start = req.Start.Value;
        if (req.End.HasValue)
            entry.End = req.End;
        entry.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(ToResponse(entry, null));
    }

    /// <summary>
    /// Удалить временную запись.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var entry = await _dbContext.TimeEntries.FindAsync(id);
        if (entry is null)
            return NotFound();

        if (!CanAccessData(currentUserId, currentRole, entry.UserId))
            return Forbid();

        _dbContext.TimeEntries.Remove(entry);
        await _dbContext.SaveChangesAsync();

        return NoContent();
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

    private List<TimeEntryResponse> GroupEventsIntoEntries(List<WorkEvent> events)
    {
        var entries = new List<TimeEntryResponse>();
        var grouped = events.GroupBy(w => w.UserId).ToList();

        foreach (var userGroup in grouped)
        {
            var userEvents = userGroup.OrderBy(w => w.Timestamp).ToList();
            WorkEvent? startWork = null;
            WorkEvent? startBreak = null;
            WorkEvent? endBreak = null;
            WorkEvent? endWork = null;

            foreach (var ev in userEvents)
            {
                switch (ev.Type)
                {
                    case WorkEventType.StartWork:
                        if (startWork == null)
                        {
                            startWork = ev;
                        }
                        break;
                    case WorkEventType.StartBreak:
                        startBreak = ev;
                        break;
                    case WorkEventType.EndBreak:
                        endBreak = ev;
                        break;
                    case WorkEventType.EndWork:
                        endWork = ev;
                        break;
                }
            }

            if (startWork != null)
            {
                entries.Add(new TimeEntryResponse
                {
                    Id = Guid.NewGuid(),
                    UserId = userGroup.Key,
                    Start = startWork.Timestamp,
                    End = endWork?.Timestamp,
                    Duration = endWork != null
                        ? endWork.Timestamp - startWork.Timestamp
                        : null,
                    EntryType = "work",
                    CreatedAt = startWork.CreatedAt
                });
            }

            if (startBreak != null)
            {
                entries.Add(new TimeEntryResponse
                {
                    Id = Guid.NewGuid(),
                    UserId = userGroup.Key,
                    Start = startBreak.Timestamp,
                    End = endBreak?.Timestamp,
                    Duration = endBreak != null
                        ? endBreak.Timestamp - startBreak.Timestamp
                        : null,
                    EntryType = "break",
                    CreatedAt = startBreak.CreatedAt
                });
            }
        }

        return entries;
    }

    private async Task<TimeEntryResponse?> GetTimeEntryById(Guid id)
    {
        var entry = await _dbContext.TimeEntries.FindAsync(id);
        if (entry is null) return null;

        var user = await _dbContext.Users.FindAsync(entry.UserId);
        return new TimeEntryResponse
        {
            Id = entry.Id,
            UserId = entry.UserId,
            UserName = user?.Email,
            Start = entry.Start,
            End = entry.End,
            Duration = entry.End.HasValue ? entry.End.Value - entry.Start : null,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt
        };
    }

    private TimeEntryResponse ToResponse(TimeEntry entry, string? userName)
    {
        return new TimeEntryResponse
        {
            Id = entry.Id,
            UserId = entry.UserId,
            UserName = userName,
            Start = entry.Start,
            End = entry.End,
            Duration = entry.End.HasValue ? entry.End.Value - entry.Start : null,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt
        };
    }
}

public class CreateTimeEntryRequest
{
    public Guid UserId { get; set; }
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
}

public class UpdateTimeEntryRequest
{
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}
