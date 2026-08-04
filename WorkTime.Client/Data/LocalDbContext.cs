using Microsoft.EntityFrameworkCore;
using WorkTime.Client.Models;

namespace WorkTime.Client.Data;

public class LocalDbContext : DbContext
{
    public DbSet<LocalWorkEvent> WorkEvents => Set<LocalWorkEvent>();

    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=worktime-local.db");
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
        });
    }
}