using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkTime.Server.Models;
using WorkTime.Server.DTOs;
using Xunit;

namespace WorkTime.Server.IntegrationTests;

public class WorkEventsIntegrationTests : IClassFixture<WorkTimeServerFactory>
{
    private readonly WorkTimeServerFactory _factory;

    // Сервер отдаёт enum строками (JsonStringEnumConverter), поэтому
    // клиентское десериализование должно использовать те же настройки.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    public WorkEventsIntegrationTests(WorkTimeServerFactory factory)
    {
        _factory = factory;
    }

    // Seed-учётная запись создаётся DbSeeder при старте приложения.
    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@worktime.local",
            password = "Admin@123"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth.Token;
    }

    [Fact]
    public async Task BatchEndpoint_IdempotencyHeader_Works()
    {
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);

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

        // HttpRequestMessage нельзя отправлять дважды — формируем отдельный запрос на каждую попытку.
        async Task<HttpResponseMessage> SendBatchAsync(string idempotencyKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/workevents/batch")
            {
                Content = JsonContent.Create(new[] { evt })
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request);
        }

        var resp1 = await SendBatchAsync("itest-1");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Repeat with same idempotency key
        var resp2 = await SendBatchAsync("itest-1");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        var body = await resp2.Content.ReadAsStringAsync();
        var batch = JsonSerializer.Deserialize<BatchSyncResponse>(body, JsonOptions);
        Assert.NotNull(batch);
        Assert.Contains(batch.Results, r => r.Id == evt.Id);
    }

    [Fact]
    public async Task Login_WithSeedAdmin_ReturnsTokenAndRole()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@worktime.local",
            password = "Admin@123"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        Assert.Equal("Admin", auth.Role);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@worktime.local",
            password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task WorkEvents_WithoutToken_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/workevents");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Devices_Create_WithClientSuppliedId_IsIdempotent()
    {
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkTime.Server.Data.WorkTimeDbContext>();
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == "admin@worktime.local");
        Assert.NotNull(admin);

        var deviceId = Guid.NewGuid();
        var payload = new { id = deviceId, userId = admin.Id, name = "Web PWA", platform = "Web" };

        var first = await client.PostAsJsonAsync("/api/devices", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Повторная регистрация того же устройства не должна создавать дубликат и не должна падать.
        var second = await client.PostAsJsonAsync("/api/devices", payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}