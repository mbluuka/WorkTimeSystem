using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkTime.Server.Models;
using WorkTime.Server.DTOs;
using Xunit;

namespace WorkTime.Server.IntegrationTests;

public class WorkEventsIntegrationTests : IClassFixture<WorkTimeServerFactory>
{
    private readonly WorkTimeServerFactory _factory;

    public WorkEventsIntegrationTests(WorkTimeServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BatchEndpoint_IdempotencyHeader_Works()
    {
        var client = _factory.CreateClient();

        // Create user and device via direct DbContext
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkTime.Server.Data.WorkTimeDbContext>();

        var user = new User { Id = Guid.NewGuid(), Name = "IUser", Email = "i@e.com" };
        var device = new Device { Id = Guid.NewGuid(), Name = "IDev", Platform = "Test", UserId = user.Id };
        db.Users.Add(user);
        db.Devices.Add(device);
        await db.SaveChangesAsync();

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

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workevents/batch");
        request.Headers.Add("Idempotency-Key", "itest-1");
        request.Content = JsonContent.Create(new[] { evt });

        var resp1 = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Repeat with same idempotency key
        var resp2 = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        var batch = await resp2.Content.ReadFromJsonAsync<BatchSyncResponse>();
        Assert.NotNull(batch);
        Assert.Contains(batch.Results, r => r.Id == evt.Id);
    }
}
