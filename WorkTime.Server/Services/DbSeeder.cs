using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;

namespace WorkTime.Server.Services;

public static class DbSeeder
{
    public static async Task SeedAsync(WorkTimeDbContext db)
    {
        // Создаём пользователя, если его нет
        var adminEmail = "admin@worktime.local";

        var exists = await db.Users.AnyAsync(u => u.Email == adminEmail);
        if (exists) return;

        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            Name = "admin",
            FullName = "Администратор системы",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Role = "Admin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync();

        // Создаём тестового сотрудника
        var empEmail = "employee@worktime.local";
        var employee = new User
        {
            Id = Guid.NewGuid(),
            Email = empEmail,
            Name = "employee",
            FullName = "Тестовый сотрудник",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Employee@123"),
            Role = "Employee",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Users.Add(employee);
        await db.SaveChangesAsync();

        // Создаём тестовое устройство для сотрудника
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            Name = "Test Device (Windows)",
            Platform = "Windows",
            CreatedAt = DateTime.UtcNow
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        Console.WriteLine($"[Seed] Admin: {adminEmail} / Admin@123");
        Console.WriteLine($"[Seed] Employee: {empEmail} / Employee@123");
    }
}
