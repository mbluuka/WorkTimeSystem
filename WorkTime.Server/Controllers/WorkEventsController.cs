using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.DTOs;
using Npgsql;
using WorkTime.Server.Models;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Microsoft.AspNetCore.Authorization.Authorize]
public class WorkEventsController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly WorkTime.Server.Services.IIdempotencyService _idempotency;
    private readonly WorkTime.Server.Services.IBulkUpsertService _bulkUpsert;

    public WorkEventsController(WorkTimeDbContext dbContext, WorkTime.Server.Services.IIdempotencyService idempotency, WorkTime.Server.Services.IBulkUpsertService bulkUpsert)
    {
        _dbContext = dbContext;
        _idempotency = idempotency;
        _bulkUpsert = bulkUpsert;
    }

    // Создание нового события рабочего времени
    [HttpPost]
    public async Task<ActionResult<WorkEventResponse>> Create(WorkEvent workEvent)
    {
        // ensure authenticated user matches userId
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        if (userId != workEvent.UserId)
        {
            return Forbid();
        }

        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(x => x.Id == workEvent.DeviceId);

        if (device is null)
        {
            return BadRequest(new
            {
                message = "Device does not exist."
            });
        }

        if (device.UserId != workEvent.UserId)
        {
            return BadRequest(new { message = "Device does not belong to the specified user." });
        }

        var existingEvent = await _dbContext.WorkEvents
            .FirstOrDefaultAsync(x => x.Id == workEvent.Id);

        if (existingEvent is not null)
        {
            return Ok(ToResponse(existingEvent));
        }

        // Ensure Id is set
        if (workEvent.Id == Guid.Empty)
        {
            workEvent.Id = Guid.NewGuid();
        }

        workEvent.SyncedAt = DateTime.UtcNow;

        workEvent.Timestamp = DateTime.SpecifyKind(
            workEvent.Timestamp,
            DateTimeKind.Utc);

        workEvent.CreatedAt = DateTime.SpecifyKind(
            workEvent.CreatedAt,
            DateTimeKind.Utc);

        if (workEvent.SyncedAt.HasValue)
        {
            workEvent.SyncedAt = DateTime.SpecifyKind(
                workEvent.SyncedAt.Value,
                DateTimeKind.Utc);
        }

        _dbContext.WorkEvents.Add(workEvent);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                // Duplicate key - return existing entity
                var existing = await _dbContext.WorkEvents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == workEvent.Id);

                if (existing is not null)
                {
                    return Conflict(new { message = "Event already exists.", id = existing.Id });
                }
            }

            throw;
        }

        var responseDto = ToResponse(workEvent);

        // Cache response for idempotency if header present
        if (Request.Headers.TryGetValue("Idempotency-Key", out var idKey) && !string.IsNullOrWhiteSpace(idKey))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(responseDto);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            _idempotency.Set(idKey, new WorkTime.Server.Services.CachedResponse(201, "application/json", bytes));
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = workEvent.Id },
            responseDto);
    }

    // Получение события по идентификатору
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkEventResponse>> GetById(Guid id)
    {
        var workEvent = await _dbContext.WorkEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (workEvent is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(workEvent));
    }

    // Получение всех событий
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> GetAll()
    {
        var events = await _dbContext.WorkEvents
            .AsNoTracking()
            .OrderBy(x => x.Timestamp)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    // Получение событий после указанного serverOrder (cursor)
    [HttpGet("events/since")]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> GetEventsSince(
        [FromQuery] Guid userId,
        [FromQuery] long? sinceServerOrder)
    {
        if (userId == Guid.Empty)
        {
            return BadRequest(new { message = "userId is required" });
        }

        var query = _dbContext.WorkEvents.AsNoTracking().Where(x => x.UserId == userId);

        if (sinceServerOrder.HasValue)
        {
            query = query.Where(x => x.ServerOrder.HasValue && x.ServerOrder > sinceServerOrder.Value);
        }

        var events = await query.OrderBy(x => x.ServerOrder).ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    // Пакетная синхронизация: принимает массив событий, возвращает статус для каждого
    [HttpPost("batch")]
    public async Task<ActionResult<BatchSyncResponse>> SyncBatch([
        FromBody] IEnumerable<WorkEvent> workEvents)
    {
        var list = workEvents?.ToList() ?? new List<WorkEvent>();

        if (!list.Any())
        {
            return BadRequest(new { message = "No events provided." });
        }

        // Normalize and set defaults
        var currentUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Employee";

        // Ensure user is allowed to sync these events
        if (currentRole != "Admin" && currentRole != "Manager")
        {
            if (list.Any(e => e.UserId != currentUserId))
            {
                return Forbid();
            }
        }

        foreach (var ev in list)
        {
            if (ev.Id == Guid.Empty)
            {
                ev.Id = Guid.NewGuid();
            }

            ev.Timestamp = DateTime.SpecifyKind(ev.Timestamp, DateTimeKind.Utc);
            ev.CreatedAt = DateTime.SpecifyKind(ev.CreatedAt, DateTimeKind.Utc);
            if (ev.SyncedAt.HasValue)
            {
                ev.SyncedAt = DateTime.SpecifyKind(ev.SyncedAt.Value, DateTimeKind.Utc);
            }
            else
            {
                ev.SyncedAt = DateTime.UtcNow;
            }
        }

        var ids = list.Select(x => x.Id).ToList();

        var existingIds = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        // Preload users and devices involved for validation
        var userIds = list.Select(x => x.UserId).Distinct().ToList();
        var deviceIds = list.Select(x => x.DeviceId).Distinct().ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var devices = await _dbContext.Devices
            .AsNoTracking()
            .Where(x => deviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var results = new List<SyncResult>();

        var toAdd = new List<WorkEvent>();

        foreach (var ev in list)
        {
            if (existingIds.Contains(ev.Id))
            {
                results.Add(new SyncResult { Id = ev.Id, Status = WorkTime.Server.DTOs.SyncResultStatus.Ignored, Message = "Duplicate" });
                continue;
            }

            if (!users.ContainsKey(ev.UserId))
            {
                results.Add(new SyncResult { Id = ev.Id, Status = WorkTime.Server.DTOs.SyncResultStatus.Invalid, Message = "User does not exist." });
                continue;
            }

            if (!devices.ContainsKey(ev.DeviceId))
            {
                results.Add(new SyncResult { Id = ev.Id, Status = WorkTime.Server.DTOs.SyncResultStatus.Invalid, Message = "Device does not exist." });
                continue;
            }

            if (devices[ev.DeviceId].UserId != ev.UserId)
            {
                results.Add(new SyncResult { Id = ev.Id, Status = WorkTime.Server.DTOs.SyncResultStatus.Invalid, Message = "Device does not belong to user." });
                continue;
            }

            toAdd.Add(ev);
            results.Add(new SyncResult { Id = ev.Id, Status = WorkTime.Server.DTOs.SyncResultStatus.Accepted });
        }

        if (toAdd.Any())
        {
            try
            {
                await _bulkUpsert.BulkUpsertAsync(toAdd);

                // Mark these as Upserted
                foreach (var r in results.Where(x => x.Status == WorkTime.Server.DTOs.SyncResultStatus.Accepted))
                {
                    r.Status = WorkTime.Server.DTOs.SyncResultStatus.Upserted;
                }

                // Cache results for idempotency if header present
                if (Request.Headers.TryGetValue("Idempotency-Key", out var idKeySuccess) && !string.IsNullOrWhiteSpace(idKeySuccess))
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(results);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    _idempotency.Set(idKeySuccess, new WorkTime.Server.Services.CachedResponse(200, "application/json", bytes));
                }
            }
            catch (Exception ex)
            {
                // On error, mark accepted ones as Error
                foreach (var r in results.Where(x => x.Status == WorkTime.Server.DTOs.SyncResultStatus.Accepted))
                {
                    r.Status = WorkTime.Server.DTOs.SyncResultStatus.Error;
                    r.Message = "Failed to save: " + ex.Message;
                }

                // Cache error result too if idempotency key present
                if (Request.Headers.TryGetValue("Idempotency-Key", out var idKeyFail) && !string.IsNullOrWhiteSpace(idKeyFail))
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(results);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    _idempotency.Set(idKeyFail, new WorkTime.Server.Services.CachedResponse(200, "application/json", bytes));
                }
            }
        }

        // After upsert (or if nothing to add), return merged timeline and lastServerOrder for the user
        var userIdForTimeline = list.First().UserId;
        var timeline = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => x.UserId == userIdForTimeline)
            .OrderBy(x => x.ServerOrder)
            .ThenBy(x => x.Timestamp)
            .ToListAsync();

        var lastOrder = timeline.Any() ? timeline.Max(x => x.ServerOrder) : null;

        var response = new BatchSyncResponse
        {
            Results = results,
            Timeline = timeline.Select(ToResponse),
            LastServerOrder = lastOrder
        };

        return Ok(response);
    }

    // Получение событий конкретного пользователя
    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> GetByUser(
    Guid userId)
    {
        var currentUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Employee";

        if (currentRole != "Admin" && currentRole != "Manager" && currentUserId != userId)
        {
            return Forbid();
        }

        var events = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    [HttpGet("sync")]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> Sync(
    [FromQuery] Guid userId,
    [FromQuery] DateTime? since)
    {
        var query = _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (since.HasValue)
        {
            query = query.Where(x => x.CreatedAt > since.Value);
        }

        var events = await query
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    // Преобразование сущности БД в DTO ответа
    private static WorkEventResponse ToResponse(WorkEvent workEvent)
    {
        return new WorkEventResponse
        {
            Id = workEvent.Id,
            UserId = workEvent.UserId,
            DeviceId = workEvent.DeviceId,
            Type = workEvent.Type,
            Timestamp = workEvent.Timestamp,
            CreatedAt = workEvent.CreatedAt,
            SyncedAt = workEvent.SyncedAt,
            ServerReceivedAt = workEvent.ServerReceivedAt,
            ServerOrder = workEvent.ServerOrder
        };
    }
}