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
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();
    public DbSet<SyncQueueEntry> SyncQueue => Set<SyncQueueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Report entity
        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.DataJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.ReportType)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

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

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasOne(x => x.Manager)
                .WithMany()
                .HasForeignKey(x => x.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.TimeEntries)
                .WithOne(x => x.Project)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasOne(x => x.Admin)
                .WithMany()
                .HasForeignKey(x => x.AdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}