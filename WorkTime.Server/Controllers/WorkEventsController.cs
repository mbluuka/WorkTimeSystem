using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkTime.Server.Data;
using WorkTime.Server.DTOs;
using WorkTime.Server.Models;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkEventsController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly WorkTime.Server.Services.IIdempotencyService _idempotency;
    private readonly WorkTime.Server.Services.IBulkUpsertService _bulkUpsert;
    private readonly ILogger<WorkEventsController> _logger;

    public WorkEventsController(
        WorkTimeDbContext dbContext,
        WorkTime.Server.Services.IIdempotencyService idempotency,
        WorkTime.Server.Services.IBulkUpsertService bulkUpsert,
        ILogger<WorkEventsController> logger)
    {
        _dbContext = dbContext;
        _idempotency = idempotency;
        _bulkUpsert = bulkUpsert;
        _logger = logger;
    }

    // ============================================================
    // CREATE
    // ============================================================

    [HttpPost]
    public async Task<ActionResult<WorkEventResponse>> Create(
        [FromBody] WorkEvent workEvent)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        if (workEvent.UserId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "UserId is required."
            });
        }

        if (workEvent.DeviceId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "DeviceId is required."
            });
        }

        // Employee can create events only for himself.
        if (currentRole != "Admin" &&
            currentRole != "Manager" &&
            workEvent.UserId != currentUserId)
        {
            return Forbid();
        }

        var userExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == workEvent.UserId);

        if (!userExists)
        {
            return BadRequest(new
            {
                message = "User does not exist."
            });
        }

        var device = await _dbContext.Devices
            .AsNoTracking()
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
            return BadRequest(new
            {
                message = "Device does not belong to the specified user."
            });
        }

        // If the event already exists, make the operation idempotent.
        if (workEvent.Id != Guid.Empty)
        {
            var existingEvent = await _dbContext.WorkEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == workEvent.Id);

            if (existingEvent is not null)
            {
                if (existingEvent.UserId != workEvent.UserId)
                {
                    return Conflict(new
                    {
                        message = "An event with this id already belongs to another user."
                    });
                }

                return Ok(ToResponse(existingEvent));
            }
        }
        else
        {
            workEvent.Id = Guid.NewGuid();
        }

        workEvent.Timestamp = EnsureUtc(workEvent.Timestamp);
        workEvent.CreatedAt = EnsureUtc(workEvent.CreatedAt);

        if (workEvent.CreatedAt == default)
        {
            workEvent.CreatedAt = DateTime.UtcNow;
        }

        workEvent.SyncedAt = DateTime.UtcNow;
        workEvent.ServerReceivedAt = DateTime.UtcNow;

        _dbContext.WorkEvents.Add(workEvent);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is PostgresException pgEx &&
                pgEx.SqlState == "23505")
            {
                var existing = await _dbContext.WorkEvents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == workEvent.Id);

                if (existing is not null)
                {
                    return Conflict(new
                    {
                        message = "Event already exists.",
                        id = existing.Id
                    });
                }
            }

            _logger.LogError(
                ex,
                "Failed to create WorkEvent {WorkEventId}",
                workEvent.Id);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "Failed to create work event."
                });
        }

        var responseDto = ToResponse(workEvent);

        CacheIdempotencyResponse(responseDto, 201);

        return CreatedAtAction(
            nameof(GetById),
            new
            {
                id = workEvent.Id
            },
            responseDto);
    }

    // ============================================================
    // GET BY ID
    // ============================================================

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkEventResponse>> GetById(Guid id)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        var workEvent = await _dbContext.WorkEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (workEvent is null)
        {
            return NotFound(new
            {
                message = "Work event not found."
            });
        }

        if (!CanAccessUserData(
                currentUserId,
                currentRole,
                workEvent.UserId))
        {
            return Forbid();
        }

        return Ok(ToResponse(workEvent));
    }

    // ============================================================
    // GET ALL
    // ============================================================

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> GetAll()
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        IQueryable<WorkEvent> query =
            _dbContext.WorkEvents.AsNoTracking();

        if (!IsPrivilegedRole(currentRole))
        {
            query = query.Where(x => x.UserId == currentUserId);
        }

        var events = await query
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    // ============================================================
    // GET EVENTS SINCE SERVER ORDER
    // ============================================================

    [HttpGet("events/since")]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> GetEventsSince(
        [FromQuery] Guid userId,
        [FromQuery] long? sinceServerOrder)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        if (userId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "userId is required."
            });
        }

        // IMPORTANT:
        // Employee can request only his own events.
        if (!CanAccessUserData(
                currentUserId,
                currentRole,
                userId))
        {
            return Forbid();
        }

        var query = _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (sinceServerOrder.HasValue)
        {
            query = query.Where(
                x => x.ServerOrder.HasValue &&
                     x.ServerOrder > sinceServerOrder.Value);
        }

        var events = await query
            .OrderBy(x => x.ServerOrder)
            .ThenBy(x => x.Timestamp)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    // ============================================================
    // BATCH SYNC
    // ============================================================

    [HttpPost("batch")]
    public async Task<ActionResult<BatchSyncResponse>> SyncBatch(
        [FromBody] IEnumerable<WorkEvent> workEvents)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        var list = workEvents?.ToList()
                   ?? new List<WorkEvent>();

        if (!list.Any())
        {
            return BadRequest(new
            {
                message = "No events provided."
            });
        }

        // Employee can synchronize only his own events.
        if (!IsPrivilegedRole(currentRole))
        {
            if (list.Any(x => x.UserId != currentUserId))
            {
                return Forbid();
            }
        }

        // --------------------------------------------------------
        // Normalize events
        // --------------------------------------------------------

        foreach (var ev in list)
        {
            if (ev.Id == Guid.Empty)
            {
                ev.Id = Guid.NewGuid();
            }

            ev.Timestamp = EnsureUtc(ev.Timestamp);
            ev.CreatedAt = EnsureUtc(ev.CreatedAt);

            if (ev.CreatedAt == default)
            {
                ev.CreatedAt = DateTime.UtcNow;
            }

            ev.SyncedAt = EnsureUtc(
                ev.SyncedAt ?? DateTime.UtcNow);

            ev.ServerReceivedAt = DateTime.UtcNow;
        }

        // --------------------------------------------------------
        // Validate duplicate IDs inside the same request
        // --------------------------------------------------------

        var duplicateIds = list
            .GroupBy(x => x.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        if (duplicateIds.Any())
        {
            return Conflict(new
            {
                message = "Request contains duplicate event IDs.",
                ids = duplicateIds
            });
        }

        // --------------------------------------------------------
        // Load existing events
        // --------------------------------------------------------

        var ids = list
            .Select(x => x.Id)
            .ToList();

        var existingEvents = await _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        // --------------------------------------------------------
        // Load users
        // --------------------------------------------------------

        var userIds = list
            .Select(x => x.UserId)
            .Distinct()
            .ToList();

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        // --------------------------------------------------------
        // Load devices
        // --------------------------------------------------------

        var deviceIds = list
            .Select(x => x.DeviceId)
            .Distinct()
            .ToList();

        var devices = await _dbContext.Devices
            .AsNoTracking()
            .Where(x => deviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var results = new List<SyncResult>();
        var toUpsert = new List<WorkEvent>();

        // --------------------------------------------------------
        // Validate each event
        // --------------------------------------------------------

        foreach (var ev in list)
        {
            if (existingEvents.TryGetValue(
                    ev.Id,
                    out var existingEvent))
            {
                // Existing event belongs to another user.
                if (existingEvent.UserId != ev.UserId)
                {
                    results.Add(new SyncResult
                    {
                        Id = ev.Id,
                        Status = SyncResultStatus.Invalid,
                        Message = "Event belongs to another user."
                    });

                    continue;
                }

                results.Add(new SyncResult
                {
                    Id = ev.Id,
                    Status = SyncResultStatus.Ignored,
                    Message = "Duplicate"
                });

                continue;
            }

            if (!users.ContainsKey(ev.UserId))
            {
                results.Add(new SyncResult
                {
                    Id = ev.Id,
                    Status = SyncResultStatus.Invalid,
                    Message = "User does not exist."
                });

                continue;
            }

            if (!devices.TryGetValue(
                    ev.DeviceId,
                    out var device))
            {
                results.Add(new SyncResult
                {
                    Id = ev.Id,
                    Status = SyncResultStatus.Invalid,
                    Message = "Device does not exist."
                });

                continue;
            }

            if (device.UserId != ev.UserId)
            {
                results.Add(new SyncResult
                {
                    Id = ev.Id,
                    Status = SyncResultStatus.Invalid,
                    Message = "Device does not belong to user."
                });

                continue;
            }

            toUpsert.Add(ev);

            results.Add(new SyncResult
            {
                Id = ev.Id,
                Status = SyncResultStatus.Accepted
            });
        }

        // --------------------------------------------------------
        // Bulk upsert
        // --------------------------------------------------------

        if (toUpsert.Any())
        {
            try
            {
                await _bulkUpsert.BulkUpsertAsync(toUpsert);

                foreach (var result in results.Where(
                             x => x.Status == SyncResultStatus.Accepted))
                {
                    result.Status = SyncResultStatus.Upserted;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to bulk upsert {Count} work events",
                    toUpsert.Count);

                foreach (var result in results.Where(
                             x => x.Status == SyncResultStatus.Accepted))
                {
                    result.Status = SyncResultStatus.Error;
                    result.Message = "Failed to save work events on server.";
                }

                CacheIdempotencyResponse(
                    results,
                    500);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new BatchSyncResponse
                    {
                        Results = results,
                        Timeline = Enumerable.Empty<WorkEventResponse>(),
                        LastServerOrder = null
                    });
            }
        }

        // --------------------------------------------------------
        // Build timeline
        //
        // For Employee:
        //     own timeline only.
        //
        // For Manager/Admin:
        //     timeline for every affected user.
        // --------------------------------------------------------

        var affectedUserIds = list
            .Select(x => x.UserId)
            .Distinct()
            .ToList();

        IQueryable<WorkEvent> timelineQuery =
            _dbContext.WorkEvents.AsNoTracking();

        if (IsPrivilegedRole(currentRole))
        {
            timelineQuery = timelineQuery.Where(
                x => affectedUserIds.Contains(x.UserId));
        }
        else
        {
            timelineQuery = timelineQuery.Where(
                x => x.UserId == currentUserId);
        }

        var timeline = await timelineQuery
            .OrderBy(x => x.ServerOrder)
            .ThenBy(x => x.Timestamp)
            .ToListAsync();

        var lastOrder = timeline
            .Where(x => x.ServerOrder.HasValue)
            .Select(x => x.ServerOrder!.Value)
            .DefaultIfEmpty()
            .Max();

        var response = new BatchSyncResponse
        {
            Results = results,
            Timeline = timeline.Select(ToResponse),
            LastServerOrder =
                timeline.Any(x => x.ServerOrder.HasValue)
                    ? lastOrder
                    : null
        };

        CacheIdempotencyResponse(
            response,
            200);

        return Ok(response);
    }

    // ============================================================
    // GET BY USER
    // ============================================================

    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> GetByUser(
        Guid userId)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        if (userId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "Invalid userId."
            });
        }

        if (!CanAccessUserData(
                currentUserId,
                currentRole,
                userId))
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

    // ============================================================
    // SYNC BY DATE
    // ============================================================

    [HttpGet("sync")]
    public async Task<ActionResult<IEnumerable<WorkEventResponse>>> Sync(
        [FromQuery] Guid userId,
        [FromQuery] DateTime? since)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
        {
            return Unauthorized();
        }

        if (userId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "userId is required."
            });
        }

        if (!CanAccessUserData(
                currentUserId,
                currentRole,
                userId))
        {
            return Forbid();
        }

        var query = _dbContext.WorkEvents
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (since.HasValue)
        {
            var sinceUtc = EnsureUtc(since.Value);

            query = query.Where(
                x => x.CreatedAt > sinceUtc);
        }

        var events = await query
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private bool TryGetCurrentUser(
        out Guid userId,
        out string role)
    {
        userId = Guid.Empty;
        role = "Employee";

        var userIdClaim = User.FindFirst(
            ClaimTypes.NameIdentifier);

        if (userIdClaim is null)
        {
            return false;
        }

        if (!Guid.TryParse(
                userIdClaim.Value,
                out userId))
        {
            return false;
        }

        role = User.FindFirst(
                   ClaimTypes.Role)?.Value
               ?? "Employee";

        return true;
    }

    private static bool IsPrivilegedRole(
        string role)
    {
        return role == "Admin" ||
               role == "Manager";
    }

    private static bool CanAccessUserData(
        Guid currentUserId,
        string currentRole,
        Guid targetUserId)
    {
        return IsPrivilegedRole(currentRole) ||
               currentUserId == targetUserId;
    }

    private static DateTime EnsureUtc(
        DateTime value)
    {
        if (value == default)
        {
            return value;
        }

        return DateTime.SpecifyKind(
            value,
            DateTimeKind.Utc);
    }

    private static WorkEventResponse ToResponse(
        WorkEvent workEvent)
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

    private void CacheIdempotencyResponse(
        object response,
        int statusCode)
    {
        if (!Request.Headers.TryGetValue(
                "Idempotency-Key",
                out var idKey))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(idKey))
        {
            return;
        }

        var json = JsonSerializer.Serialize(response);

        var bytes = Encoding.UTF8.GetBytes(json);

        _idempotency.Set(
            idKey!,
            new WorkTime.Server.Services.CachedResponse(
                statusCode,
                "application/json",
                bytes));
    }
}