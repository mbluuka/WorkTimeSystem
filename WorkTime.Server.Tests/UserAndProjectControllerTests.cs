using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTime.Server.Controllers;
using WorkTime.Server.Data;
using WorkTime.Server.DTOs;
using WorkTime.Server.Models;
using Xunit;

namespace WorkTime.Server.Tests;

/// <summary>
/// Права администратора над пользователями и проектами.
/// PUT /api/users/{id} раньше принимал IsBlocked, но молча его игнорировал:
/// эти тесты фиксируют, что блокировка применяется через обычный Update.
/// </summary>
public class UserAndProjectControllerTests
{
    private static WorkTimeDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<WorkTimeDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new WorkTimeDbContext(options);
    }

    private static void Auth(ControllerBase controller, Guid userId, string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            },
            authenticationType: "Test");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private static User MakeUser(string email, string role = "Employee") => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        Name = email.Split('@')[0],
        FullName = "Тест",
        Role = role,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ============================================================
    // PUT /api/users/{id} — IsBlocked
    // ============================================================

    [Fact]
    public async Task Update_AppliesIsBlocked_True()
    {
        var ctx = CreateInMemoryDb("users_block_true");
        var admin = MakeUser("admin@t.local", "Admin");
        var target = MakeUser("emp@t.local");

        await ctx.Users.AddRangeAsync(admin, target);
        await ctx.SaveChangesAsync();

        var controller = new UsersController(ctx, NullLogger<UsersController>.Instance);
        Auth(controller, admin.Id, "Admin");

        var result = await controller.Update(target.Id, new UpdateUserRequest { IsBlocked = true });

        Assert.IsType<OkObjectResult>(result.Result);
        var saved = await ctx.Users.SingleAsync(u => u.Id == target.Id);
        Assert.True(saved.IsBlocked);
    }

    [Fact]
    public async Task Update_AppliesIsBlocked_False()
    {
        var ctx = CreateInMemoryDb("users_block_false");
        var admin = MakeUser("admin@t.local", "Admin");
        var target = MakeUser("emp@t.local");
        target.IsBlocked = true;

        await ctx.Users.AddRangeAsync(admin, target);
        await ctx.SaveChangesAsync();

        var controller = new UsersController(ctx, NullLogger<UsersController>.Instance);
        Auth(controller, admin.Id, "Admin");

        await controller.Update(target.Id, new UpdateUserRequest { IsBlocked = false });

        var saved = await ctx.Users.SingleAsync(u => u.Id == target.Id);
        Assert.False(saved.IsBlocked);
    }

    [Fact]
    public async Task Update_IsBlockedNull_KeepsCurrentState()
    {
        var ctx = CreateInMemoryDb("users_block_null");
        var admin = MakeUser("admin@t.local", "Admin");
        var target = MakeUser("emp@t.local");
        target.IsBlocked = true;

        await ctx.Users.AddRangeAsync(admin, target);
        await ctx.SaveChangesAsync();

        var controller = new UsersController(ctx, NullLogger<UsersController>.Instance);
        Auth(controller, admin.Id, "Admin");

        // Только ФИО — блокировка не должна измениться.
        await controller.Update(target.Id, new UpdateUserRequest { FullName = "Новое ФИО" });

        var saved = await ctx.Users.SingleAsync(u => u.Id == target.Id);
        Assert.True(saved.IsBlocked);
        Assert.Equal("Новое ФИО", saved.FullName);
    }

    [Fact]
    public async Task Update_CannotBlockOwnAccount()
    {
        var ctx = CreateInMemoryDb("users_block_self");
        var admin = MakeUser("admin@t.local", "Admin");

        await ctx.Users.AddAsync(admin);
        await ctx.SaveChangesAsync();

        var controller = new UsersController(ctx, NullLogger<UsersController>.Instance);
        Auth(controller, admin.Id, "Admin");

        var result = await controller.Update(admin.Id, new UpdateUserRequest { IsBlocked = true });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        var saved = await ctx.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.False(saved.IsBlocked);
    }

    [Fact]
    public async Task Update_Manager_CannotEditUsers()
    {
        var ctx = CreateInMemoryDb("users_manager_forbid");
        var manager = MakeUser("mgr@t.local", "Manager");
        var target = MakeUser("emp@t.local");

        await ctx.Users.AddRangeAsync(manager, target);
        await ctx.SaveChangesAsync();

        var controller = new UsersController(ctx, NullLogger<UsersController>.Instance);
        Auth(controller, manager.Id, "Manager");

        var result = await controller.Update(target.Id, new UpdateUserRequest { IsBlocked = true });

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Update_InvalidRole_IsRejected()
    {
        var ctx = CreateInMemoryDb("users_bad_role");
        var admin = MakeUser("admin@t.local", "Admin");
        var target = MakeUser("emp@t.local");

        await ctx.Users.AddRangeAsync(admin, target);
        await ctx.SaveChangesAsync();

        var controller = new UsersController(ctx, NullLogger<UsersController>.Instance);
        Auth(controller, admin.Id, "Admin");

        var result = await controller.Update(target.Id, new UpdateUserRequest { Role = "Superuser" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ============================================================
    // DELETE /api/projects/{id} — защита FK
    // ============================================================

    [Fact]
    public async Task DeleteProject_WithWorkEvents_ReturnsBadRequest()
    {
        var ctx = CreateInMemoryDb("projects_delete_blocked");
        var admin = MakeUser("admin@t.local", "Admin");
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = admin.Id,
            Name = "Dev",
            Platform = "Test",
            CreatedAt = DateTime.UtcNow
        };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Занятый проект",
            ManagerId = admin.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await ctx.Users.AddAsync(admin);
        await ctx.Devices.AddAsync(device);
        await ctx.Projects.AddAsync(project);
        await ctx.SaveChangesAsync();

        await ctx.WorkEvents.AddAsync(new WorkEvent
        {
            Id = Guid.NewGuid(),
            UserId = admin.Id,
            DeviceId = device.Id,
            Type = WorkEventType.StartWork,
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ProjectId = project.Id
        });
        await ctx.SaveChangesAsync();

        var controller = new ProjectsController(ctx, NullLogger<ProjectsController>.Instance);
        Auth(controller, admin.Id, "Admin");

        var result = await controller.Delete(project.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await ctx.Projects.FindAsync(project.Id));
    }

    [Fact]
    public async Task DeleteProject_WithoutWorkEvents_Succeeds()
    {
        var ctx = CreateInMemoryDb("projects_delete_ok");
        var admin = MakeUser("admin@t.local", "Admin");
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Пустой проект",
            ManagerId = admin.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await ctx.Users.AddAsync(admin);
        await ctx.Projects.AddAsync(project);
        await ctx.SaveChangesAsync();

        var controller = new ProjectsController(ctx, NullLogger<ProjectsController>.Instance);
        Auth(controller, admin.Id, "Admin");

        var result = await controller.Delete(project.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await ctx.Projects.FindAsync(project.Id));
    }

    [Fact]
    public async Task UpdateProject_RenamesProject()
    {
        var ctx = CreateInMemoryDb("projects_update_ok");
        var admin = MakeUser("admin@t.local", "Admin");
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Старое имя",
            Description = "Старое описание",
            ManagerId = admin.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await ctx.Users.AddAsync(admin);
        await ctx.Projects.AddAsync(project);
        await ctx.SaveChangesAsync();

        var controller = new ProjectsController(ctx, NullLogger<ProjectsController>.Instance);
        Auth(controller, admin.Id, "Admin");

        var result = await controller.Update(
            project.Id,
            new UpdateProjectRequest { Name = "Новое имя", Description = "Новое описание" });

        Assert.IsType<OkObjectResult>(result.Result);
        var saved = await ctx.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("Новое имя", saved.Name);
        Assert.Equal("Новое описание", saved.Description);
    }
}
