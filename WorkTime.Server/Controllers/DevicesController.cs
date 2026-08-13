using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.Models;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;

    public DevicesController(WorkTimeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeviceResponse>>> GetAll()
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        IQueryable<Device> query = _dbContext.Devices.AsNoTracking();

        // Employee sees only own devices
        if (currentRole != "Admin" && currentRole != "Manager")
        {
            query = query.Where(d => d.UserId == currentUserId);
        }

        var devices = await query
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();

        return Ok(devices.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceResponse>> GetById(Guid id)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);

        if (device is null)
            return NotFound();

        if (currentRole != "Admin" && currentRole != "Manager" && device.UserId != currentUserId)
            return Forbid();

        return Ok(ToResponse(device));
    }

    [HttpPost]
    public async Task<ActionResult<DeviceResponse>> Create([FromBody] CreateDeviceRequest req)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        // Employee can register device only for himself
        if (currentRole != "Admin" && currentRole != "Manager" && req.UserId != currentUserId)
            return Forbid();

        var userExists = await _dbContext.Users.AnyAsync(u => u.Id == req.UserId);
        if (!userExists)
            return BadRequest(new { message = "User does not exist." });

        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = req.UserId,
            Name = req.Name ?? "Unknown device",
            Platform = req.Platform ?? "Unknown",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Devices.Add(device);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = device.Id }, ToResponse(device));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var device = await _dbContext.Devices.FindAsync(id);
        if (device is null)
            return NotFound();

        if (currentRole != "Admin" && currentRole != "Manager" && device.UserId != currentUserId)
            return Forbid();

        _dbContext.Devices.Remove(device);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

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

    private static DeviceResponse ToResponse(Device device)
    {
        return new DeviceResponse
        {
            Id = device.Id,
            UserId = device.UserId,
            Name = device.Name,
            Platform = device.Platform,
            CreatedAt = device.CreatedAt
        };
    }
}

public class CreateDeviceRequest
{
    public Guid UserId { get; set; }
    public string? Name { get; set; }
    public string? Platform { get; set; }
}

public class DeviceResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}