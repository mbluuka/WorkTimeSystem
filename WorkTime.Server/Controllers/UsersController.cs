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
public class UsersController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly ILogger<UsersController> _logger;

    public UsersController(WorkTimeDbContext dbContext, ILogger<UsersController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // Manager видит состав команды (FR13), но учётные записи администраторов
    // ему не показываются — это область видимости Admin.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetAll()
    {
        if (!TryGetCurrentUser(out _, out var currentRole))
            return Unauthorized();

        IQueryable<User> query = _dbContext.Users.AsNoTracking();

        if (currentRole != "Admin")
            query = query.Where(u => u.Role == "Employee" || u.Role == "Manager");

        var users = await query
            .OrderBy(u => u.Email)
            .ToListAsync();

        return Ok(users.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id)
    {
        if (!TryGetCurrentUser(out _, out var currentRole))
            return Unauthorized();

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
            return NotFound();

        if (currentRole != "Admin" && user.Role == "Admin")
            return Forbid();

        return Ok(ToResponse(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create([FromBody] CreateUserRequest req)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Email and password required" });

        if (!IsValidRole(req.Role))
            return BadRequest(new { message = "Invalid role. Allowed: Employee, Manager, Admin" });

        var exists = await _dbContext.Users.AnyAsync(u => u.Email == req.Email);
        if (exists)
            return Conflict(new { message = "Email already registered" });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = req.Email,
            Name = req.Email.Split('@')[0],
            FullName = req.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        await LogAudit(currentUserId, "USER_CREATED", user.Id, $"Created user {user.Email} with role {user.Role}");

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToResponse(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, [FromBody] UpdateUserRequest req)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        var user = await _dbContext.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        if (req.FullName != null)
            user.FullName = req.FullName;

        if (req.Role != null)
        {
            if (!IsValidRole(req.Role))
                return BadRequest(new { message = "Invalid role" });
            user.Role = req.Role;
        }

        // Раньше здесь обновлялся только UpdatedAt, а IsBlocked игнорировался:
        // PUT с {"isBlocked": true} возвращал 200, но пользователя не блокировал.
        if (req.IsBlocked.HasValue && user.IsBlocked != req.IsBlocked.Value)
        {
            if (req.IsBlocked.Value && user.Id == currentUserId)
                return BadRequest(new { message = "Cannot block your own account" });

            user.IsBlocked = req.IsBlocked.Value;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await LogAudit(currentUserId, "USER_UPDATED", user.Id, $"Updated user {user.Email}");

        return Ok(ToResponse(user));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        var user = await _dbContext.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        // Prevent self-deletion
        if (user.Id == currentUserId)
            return BadRequest(new { message = "Cannot delete your own account" });

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        await LogAudit(currentUserId, "USER_DELETED", user.Id, $"Deleted user {user.Email}");

        return NoContent();
    }

    [HttpPost("{id:guid}/block")]
    public async Task<ActionResult<UserResponse>> Block(Guid id)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        var user = await _dbContext.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        if (user.Id == currentUserId)
            return BadRequest(new { message = "Cannot block your own account" });

        user.IsBlocked = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await LogAudit(currentUserId, "USER_BLOCKED", user.Id, $"Blocked user {user.Email}");

        return Ok(ToResponse(user));
    }

    [HttpPost("{id:guid}/unblock")]
    public async Task<ActionResult<UserResponse>> Unblock(Guid id)
    {
        if (!TryGetAdmin(out var currentUserId))
            return Forbid();

        var user = await _dbContext.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        user.IsBlocked = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await LogAudit(currentUserId, "USER_UNBLOCKED", user.Id, $"Unblocked user {user.Email}");

        return Ok(ToResponse(user));
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

    private bool TryGetAdmin(out Guid currentUserId)
    {
        currentUserId = Guid.Empty;

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out currentUserId))
            return false;

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Employee";
        return role == "Admin";
    }

    private static bool IsValidRole(string? role)
    {
        return role is "Employee" or "Manager" or "Admin";
    }

    private async Task LogAudit(Guid adminId, string action, Guid? targetUserId, string? details)
    {
        var admin = await _dbContext.Users.FindAsync(adminId);
        var target = targetUserId.HasValue ? await _dbContext.Users.FindAsync(targetUserId.Value) : null;

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            AdminId = adminId,
            Action = action,
            TargetUserId = targetUserId,
            TargetUserName = target?.Email,
            Details = details,
            Timestamp = DateTime.UtcNow
        };

        _dbContext.AuditLogs.Add(log);
        await _dbContext.SaveChangesAsync();
    }

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            FullName = user.FullName,
            Role = user.Role,
            IsBlocked = user.IsBlocked,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}
