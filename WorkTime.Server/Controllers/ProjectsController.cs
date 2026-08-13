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
public class ProjectsController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(WorkTimeDbContext dbContext, ILogger<ProjectsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectResponse>>> GetAll()
    {
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Ok(projects.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> GetById(Guid id)
    {
        var project = await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null)
            return NotFound();

        return Ok(ToResponse(project));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest req)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Project name is required" });

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            ManagerId = currentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToResponse(project));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Update(Guid id, [FromBody] UpdateProjectRequest req)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var project = await _dbContext.Projects.FindAsync(id);
        if (project is null)
            return NotFound();

        // Only manager or admin can update
        if (currentRole != "Admin" && project.ManagerId != currentUserId)
            return Forbid();

        if (req.Name != null)
            project.Name = req.Name;
        if (req.Description != null)
            project.Description = req.Description;
        project.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(ToResponse(project));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetCurrentUser(out var currentUserId, out var currentRole))
            return Unauthorized();

        var project = await _dbContext.Projects.FindAsync(id);
        if (project is null)
            return NotFound();

        if (currentRole != "Admin" && project.ManagerId != currentUserId)
            return Forbid();

        _dbContext.Projects.Remove(project);
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

    private static ProjectResponse ToResponse(Project project)
    {
        return new ProjectResponse
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            ManagerId = project.ManagerId,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }
}
