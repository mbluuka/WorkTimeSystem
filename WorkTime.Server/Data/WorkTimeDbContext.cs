using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Models;

namespace WorkTime.Server.Data;

public class WorkTimeDbContext : DbContext
{
    public WorkTimeDbContext(DbContextOptions<WorkTimeDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<WorkEvent> WorkEvents => Set<WorkEvent>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();
    public DbSet<SyncQueueEntry> SyncQueue => Set<SyncQueueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasMany(x => x.Devices)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.WorkEvents)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.Start });
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Platform)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasMany(x => x.WorkEvents)
                .WithOne(x => x.Device)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkEvent>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.UserId,
                x.Timestamp
            });

            entity.HasIndex(x => new
            {
                x.DeviceId,
                x.Timestamp
            });

            entity.Property(x => x.SyncedAt);
        });

        modelBuilder.Entity<IdempotencyEntry>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Body).HasColumnType("bytea");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<SyncQueueEntry>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Data)
                .HasColumnType("jsonb");

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("now()");

            entity.HasIndex(x => new
            {
                x.UserId,
                x.EntityType
            });

            entity.HasIndex(x => new
            {
                x.ProcessedAt,
                x.NextRetry,
                x.Attempts
            });
        });
    }
}