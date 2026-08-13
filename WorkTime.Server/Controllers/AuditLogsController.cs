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
public class AuditLogsController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(WorkTimeDbContext dbContext, ILogger<AuditLogsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        var query = _dbContext.AuditLogs
            .AsNoTracking();

        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.Timestamp <= to.Value);

        var totalCount = await query.CountAsync();
        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var response = logs.Select(l => new AuditLogResponse
        {
            Id = l.Id,
            AdminId = l.AdminId,
            AdminName = l.Admin?.Email,
            Action = l.Action,
            TargetUserId = l.TargetUserId,
            TargetUserName = l.TargetUserName,
            Details = l.Details,
            Timestamp = l.Timestamp
        });

        return Ok(new
        {
            Items = response,
            Total = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditLogResponse>> GetById(Guid id)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        var log = await _dbContext.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);

        if (log is null)
            return NotFound();

        return Ok(new AuditLogResponse
        {
            Id = log.Id,
            AdminId = log.AdminId,
            AdminName = log.Admin?.Email,
            Action = log.Action,
            TargetUserId = log.TargetUserId,
            TargetUserName = log.TargetUserName,
            Details = log.Details,
            Timestamp = log.Timestamp
        });
    }

    private bool TryGetAdmin(out Guid currentUserId)
    {
        currentUserId = Guid.Empty;

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out currentUserId))
            return false;

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Employee";
        return role == "Admin";
    }
}
