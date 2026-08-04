using System.Text;
using Microsoft.AspNetCore.Http;
using WorkTime.Server.Services;

namespace WorkTime.Server.Middleware;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) || string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            await _next(context);
            return;
        }

        var key = keyValues.First();

        // Only cache POST/PUT/PATCH
        if (!(context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
              || context.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
              || context.Request.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var service = context.RequestServices.GetService(typeof(WorkTime.Server.Services.IIdempotencyService)) as WorkTime.Server.Services.IIdempotencyService;
        if (service is not null && service.TryGet(key, out var cached))
        {
            context.Response.StatusCode = cached.StatusCode;
            if (!string.IsNullOrEmpty(cached.ContentType))
            {
                context.Response.ContentType = cached.ContentType;
            }

            await context.Response.Body.WriteAsync(cached.Body, 0, cached.Body.Length);
            return;
        }

        // Capture response
        var originalBody = context.Response.Body;
        await using var ms = new MemoryStream();
        context.Response.Body = ms;

        await _next(context);

        // Read response
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var bodyBytes = ms.ToArray();

        // Cache successful responses (2xx)
        if (service is not null && context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            var contentType = context.Response.ContentType ?? "application/json";
            var cachedResp = new CachedResponse(context.Response.StatusCode, contentType, bodyBytes);
            service.Set(key, cachedResp);
        }

        // Write back to original stream
        context.Response.Body = originalBody;
        await context.Response.Body.WriteAsync(bodyBytes, 0, bodyBytes.Length);
    }
}
