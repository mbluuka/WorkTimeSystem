using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WorkTime.Server.Controllers;
using WorkTime.Server.Data;
using WorkTime.Server.Models;
using WorkTime.Server.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace WorkTime.Server.Tests;

public class SyncTests
{
    private WorkTimeDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<WorkTimeDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var ctx = new WorkTimeDbContext(options);
        return ctx;
    }

    [Fact]
    public async Task Batch_Duplicate_Is_Ignored()
    {
        var ctx = CreateInMemoryDb("dup_test");

        var user = new User { Id = Guid.NewGuid(), Name = "Test", Email = "a@b.com" };
        var device = new Device { Id = Guid.NewGuid(), Name = "Dev", Platform = "Test", UserId = user.Id };

        await ctx.Users.AddAsync(user);
        await ctx.Devices.AddAsync(device);

        var existing = new WorkEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = device.Id,
            Type = WorkEventType.StartWork,
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            SyncedAt = DateTime.UtcNow
        };

        await ctx.WorkEvents.AddAsync(existing);
        await ctx.SaveChangesAsync();

        var newEvent = new WorkEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = device.Id,
            Type = WorkEventType.EndWork,
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            SyncedAt = DateTime.UtcNow
        };

        var idemp = new DbIdempotencyService(ctx);

        var bulk = new WorkTime.Server.Services.BulkUpsertService(ctx);
        var controller = new WorkEventsController(ctx, idemp, bulk);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var batch = new[] { existing, newEvent };

        var result = await controller.SyncBatch(batch);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var batchResp = Assert.IsType<WorkTime.Server.DTOs.BatchSyncResponse>(ok.Value);
        var list = batchResp.Results;
        Assert.Contains(list, r => r.Id == existing.Id && r.Status == WorkTime.Server.DTOs.SyncResultStatus.Ignored);
        Assert.Contains(list, r => r.Id == newEvent.Id && (r.Status == WorkTime.Server.DTOs.SyncResultStatus.Upserted || r.Status == WorkTime.Server.DTOs.SyncResultStatus.Accepted));
    }

    [Fact]
    public async Task IdempotencyKey_Caches_Result()
    {
        var ctx = CreateInMemoryDb("idem_test");

        var user = new User { Id = Guid.NewGuid(), Name = "Test", Email = "a@b.com" };
        var device = new Device { Id = Guid.NewGuid(), Name = "Dev", Platform = "Test", UserId = user.Id };

        await ctx.Users.AddAsync(user);
        await ctx.Devices.AddAsync(device);
        await ctx.SaveChangesAsync();

        var evt = new WorkEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = device.Id,
            Type = WorkEventType.StartWork,
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            SyncedAt = DateTime.UtcNow
        };

        var idemp = new DbIdempotencyService(ctx);

        var bulk2 = new WorkTime.Server.Services.BulkUpsertService(ctx);
        var controller = new WorkEventsController(ctx, idemp, bulk2);
        var http = new DefaultHttpContext();
        http.Request.Headers["Idempotency-Key"] = "key-123";
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = http
        };

        var batch = new[] { evt };

        var first = await controller.SyncBatch(batch);
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(first.Result);
        var batchResp = Assert.IsType<WorkTime.Server.DTOs.BatchSyncResponse>(ok.Value);

        // Ensure cache filled
        Assert.True(idemp.TryGet("key-123", out var cached));
        Assert.NotNull(cached);
        var json = Encoding.UTF8.GetString(cached!.Body);
        Assert.Contains(evt.Id.ToString(), json);
    }
}
