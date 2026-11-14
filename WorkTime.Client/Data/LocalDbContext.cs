using Microsoft.EntityFrameworkCore;
using WorkTime.Client.Models;

namespace WorkTime.Client.Data;

public class LocalDbContext : DbContext
{
    // По умолчанию — файл рядом с исполняемым файлом (консольный клиент).
    // Десктоп-клиент передаёт путь в профиле пользователя.
    public static string DefaultDatabasePath => "worktime-local.db";

    private readonly string _databasePath;

    public LocalDbContext()
        : this(DefaultDatabasePath)
    {
    }

    public LocalDbContext(string databasePath)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? DefaultDatabasePath
            : databasePath;
    }

    public DbSet<LocalWorkEvent> WorkEvents => Set<LocalWorkEvent>();

    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalWorkEvent>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.UserId)
                .IsRequired();

            entity.Property(x => x.DeviceId)
                .IsRequired();

            entity.Property(x => x.Type)
                .HasConversion<int>();

            entity.Property(x => x.Timestamp)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.SyncStatus)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.DeviceSeq)
                .IsRequired();

            entity.Property(x => x.ProjectId);
        });
    }
}
