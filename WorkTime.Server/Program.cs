using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.Services;
using System.IO;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WorkTimeDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("WorkTimeDatabase")));

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Введите JWT-токен. Формат: Bearer {token}"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // WorkEventType отдаётся и принимается строками ("StartWork"),
        // иначе фронтенд сравнивает числовые значения enum со строками и ломается
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Authentication & Authorization (JWT)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Issuer"],
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "dev-key-change"))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireManager", policy => policy.RequireRole("Manager", "Admin"));
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
});

// Idempotency and caching
builder.Services.AddMemoryCache();
// Register DB-backed idempotency service as scoped (uses DbContext)
builder.Services.AddScoped<WorkTime.Server.Services.IIdempotencyService, WorkTime.Server.Services.DbIdempotencyService>();
builder.Services.AddScoped<WorkTime.Server.Services.IBulkUpsertService, WorkTime.Server.Services.BulkUpsertService>();
// Background cleanup for idempotency entries
builder.Services.AddHostedService<WorkTime.Server.Services.IdempotencyCleanupService>();

// SyncQueue processor
builder.Services.Configure<WorkTime.Server.Services.SyncQueueOptions>(builder.Configuration.GetSection("SyncQueue"));
builder.Services.AddHostedService<WorkTime.Server.Services.SyncQueueProcessor>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// Authentication/Authorization
app.UseAuthentication();
app.UseAuthorization();

// Idempotency middleware: caches responses for requests with Idempotency-Key header
app.UseMiddleware<WorkTime.Server.Middleware.IdempotencyMiddleware>();

// Serve React PWA static files
// В продакшене (Docker) — wwwroot уже лежит в ContentRoot/wwwroot
// В разработке — ищем dist в соседней папке WorkTime.WebReact/dist
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (!Directory.Exists(wwwrootPath))
{
    wwwrootPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "WorkTime.WebReact", "dist"));
}

if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath),
        DefaultFileNames = new List<string> { "index.html" }
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwrootPath),
        RequestPath = ""
    });
}

app.MapControllers();

// Apply EF migrations at startup (auto-apply)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<WorkTimeDbContext>();
        db.Database.Migrate();
        await DbSeeder.SeedAsync(db);
    }
    catch (Exception ex)
    {
        // Migration errors should be visible in logs; swallow to allow app to start in limited scenarios
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Migration");
        logger?.LogError(ex, "Failed to migrate database on startup.");
    }
}

app.Run();

// Точка входа из top-level statements внутренняя;
// объявление делает её доступной для WebApplicationFactory в интеграционных тестах.
public partial class Program { }