using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.Models;

namespace WorkTime.Server.Services;

public static class DbSeeder
{
    // Seed проверяется по email, а не «есть ли вообще пользователи»:
    // так добавление нового seed-аккаунта не требует чистой базы.
    public static async Task SeedAsync(WorkTimeDbContext db)
    {
        var admin = await EnsureUserAsync(db, "admin@worktime.local", "admin",
            "Администратор системы", "Admin@123", "Admin");

        var manager = await EnsureUserAsync(db, "manager@worktime.local", "manager",
            "Руководитель отдела", "Manager@123", "Manager");

        var employee = await EnsureUserAsync(db, "employee@worktime.local", "employee",
            "Тестовый сотрудник", "Employee@123", "Employee");

        await EnsureDeviceAsync(db, employee.Id, "Test Device (Windows)", "Windows");
        await EnsureDeviceAsync(db, manager.Id, "Manager Device (Windows)", "Windows");

        // Проекты нужны, чтобы сводный отчёт по проектам (FR14) был проверяем сразу после запуска.
        await EnsureProjectAsync(db, "Внутренние процессы", "Администрирование и поддержка", admin.Id);
        await EnsureProjectAsync(db, "Разработка мобильного клиента", "Flutter-приложение для Android", manager.Id);
        await EnsureProjectAsync(db, "Десктоп для RED OS", "Avalonia-клиент под отечественные ОС", manager.Id);

        Console.WriteLine("[Seed] Admin: admin@worktime.local / Admin@123");
        Console.WriteLine("[Seed] Manager: manager@worktime.local / Manager@123");
        Console.WriteLine("[Seed] Employee: employee@worktime.local / Employee@123");
    }

    private static async Task<User> EnsureUserAsync(
        WorkTimeDbContext db,
        string email,
        string name,
        string fullName,
        string password,
        string role)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
            return existing;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = name,
            FullName = fullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    private static async Task EnsureDeviceAsync(
        WorkTimeDbContext db,
        Guid userId,
        string deviceName,
        string platform)
    {
        var exists = await db.Devices.AnyAsync(d => d.UserId == userId && d.Name == deviceName);
        if (exists)
            return;

        db.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = deviceName,
            Platform = platform,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task EnsureProjectAsync(
        WorkTimeDbContext db,
        string projectName,
        string? description,
        Guid managerId)
    {
        var exists = await db.Projects.AnyAsync(p => p.Name == projectName);
        if (exists)
            return;

        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(),
            Name = projectName,
            Description = description,
            ManagerId = managerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }
}
