using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WorkTime.Client.Models;
using WorkTime.Client.Storage;

namespace WorkTime.Desktop.Services;

public record AuthResponse(string Token, string Role);

public record ProjectInfo(Guid Id, string Name, string? Description);

/// <summary>
/// Тонкий HTTP-слой над серверным API. Локальное хранение и очередь
/// синхронизации обеспечивает WorkTime.Client (SQLite), здесь только
/// аутентификация, устройства и справочники.
/// </summary>
public class DesktopApiClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly HttpClient _http;

    public DesktopApiClient(string baseUrl)
    {
        BaseUrl = Normalize(baseUrl);
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public string BaseUrl { get; private set; }

    /// <summary>
    /// Меняет адрес сервера «на лету» — окно логина позволяет указать другой
    /// хост без перезапуска клиента. Токен при этом сбрасывается: он выдан
    /// другим сервером.
    /// </summary>
    public void Rebase(string baseUrl)
    {
        var normalized = Normalize(baseUrl);
        if (string.Equals(normalized, BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        BaseUrl = normalized;
        _http.BaseAddress = new Uri(normalized);
        SetToken(null);
    }

    private static string Normalize(string baseUrl) => baseUrl.TrimEnd('/') + "/";

    public string? Token { get; private set; }

    public void SetToken(string? token)
    {
        Token = token;
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<AuthResponse?> LoginAsync(string email, string password)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login",
            new { email, password }, JsonOptions);

        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        if (auth is not null)
        {
            SetToken(auth.Token);
        }

        return auth;
    }

    public async Task RegisterDeviceAsync(Guid deviceId, Guid userId, string name, string platform)
    {
        // Ищем на сервере ИМЕННО это устройство (stable device.id из local-state),
        // а не «любое устройство пользователя»: у одного пользователя их несколько
        // (веб, десктоп), а события привязаны по FK к конкретному DeviceId.
        try
        {
            var devices = await _http.GetFromJsonAsync<List<DeviceInfo>>("api/devices", JsonOptions);
            var own = devices?.Find(d => d.Id == deviceId);
            if (own is not null)
            {
                return;
            }
        }
        catch
        {
            // нет сети — deviceId всё равно подойдёт для локальных событий
        }

        try
        {
            // POST идемпотентен на сервере: при существующем Id вернёт 200.
            await _http.PostAsJsonAsync("api/devices",
                new { id = deviceId, userId, name, platform }, JsonOptions);
        }
        catch
        {
            // офлайн: устройство зарегистрируется при первой успешной синхронизации
        }
    }

    public async Task<List<ProjectInfo>> GetProjectsAsync()
    {
        try
        {
            var projects = await _http.GetFromJsonAsync<List<ProjectInfo>>("api/projects", JsonOptions);
            return projects ?? new List<ProjectInfo>();
        }
        catch
        {
            return new List<ProjectInfo>();
        }
    }

    /// <summary>
    /// Отправляет локальную очередь Pending на сервер. Общий local-first слой
    /// (WorkTime.Client) обеспечивает идемпотентность и порядок deviceSeq.
    /// Бросает исключение при отсутствии связи — GUI показывает офлайн-статус.
    /// </summary>
    public async Task SyncPendingEventsAsync(LocalEventStore store)
    {
        var pending = await store.GetPendingAsync();

        foreach (var workEvent in pending)
        {
            var resp = await PostWorkEventAsync(workEvent);

            if (!resp.IsSuccessStatusCode)
            {
                var error = await resp.Content.ReadAsStringAsync();

                // Самолечение: если вход был в офлайне, устройство не
                // зарегистрировалось и сервер отклоняет события. Регистрируем
                // его по данным самого события и повторяем отправку один раз.
                if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                    error.Contains("Device does not exist", StringComparison.OrdinalIgnoreCase))
                {
                    await RegisterDeviceAsync(
                        workEvent.DeviceId,
                        workEvent.UserId,
                        $"{Environment.MachineName} (Desktop)",
                        OperatingSystem.IsWindows() ? "Windows" : "Linux");

                    resp = await PostWorkEventAsync(workEvent);
                    error = resp.IsSuccessStatusCode ? string.Empty : await resp.Content.ReadAsStringAsync();
                }

                if (!resp.IsSuccessStatusCode)
                {
                    // Очередь остаётся Pending — повторим при следующей синхронизации.
                    throw new InvalidOperationException(
                        $"HTTP {(int)resp.StatusCode}: {error}");
                }
            }

            await store.MarkAsSyncedAsync(workEvent.Id, DateTime.UtcNow);
        }
    }

    private Task<HttpResponseMessage> PostWorkEventAsync(LocalWorkEvent workEvent) =>
        _http.PostAsJsonAsync("api/WorkEvents", new
        {
            id = workEvent.Id,
            userId = workEvent.UserId,
            deviceId = workEvent.DeviceId,
            type = workEvent.Type,
            timestamp = workEvent.Timestamp,
            createdAt = workEvent.CreatedAt,
            deviceSeq = workEvent.DeviceSeq,
            projectId = workEvent.ProjectId
        }, JsonOptions);

    private record DeviceInfo(Guid Id, Guid UserId, string Name, string Platform);

    /// <summary>
    /// Имена claim'ов, в которых сервер может нести идентификатор пользователя.
    /// JwtSecurityTokenHandler (используется на сервере) пишет полные URI-имена,
    /// а JsonWebTokenHandler — короткие "nameid"/"sub", поэтому перебираем все.
    /// </summary>
    private static readonly string[] UserIdClaimNames =
    {
        "nameid",
        "sub",
        "NameIdentifier",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    };

    /// <summary>
    /// Извлекает userId из JWT без верификации подписи — токен уже выдан
    /// серверу этим же логином, так что доверять ему локально безопасно.
    /// </summary>
    public static Guid DecodeUserId(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return Guid.Empty;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty
            };

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            foreach (var name in UserIdClaimNames)
            {
                if (doc.RootElement.TryGetProperty(name, out var el) &&
                    Guid.TryParse(el.GetString(), out var id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // ignore
        }

        return Guid.Empty;
    }
}
