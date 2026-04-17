using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;
using Platform.Application.ModuleState;
using Platform.Domain.ModuleActivation;

namespace Platform.Infrastructure.Persistence;

public sealed class PlatformPersistenceDbContext : DbContext
{
    private static readonly ValueConverter<Instant, DateTimeOffset> InstantValueConverter = new(
        static instant => instant.ToDateTimeOffset(),
        static dateTimeOffset => Instant.FromDateTimeOffset(dateTimeOffset));

    public PlatformPersistenceDbContext(DbContextOptions<PlatformPersistenceDbContext> options)
        : base(options)
    {
    }

    internal DbSet<PlatformAuditEventRecord> AuditEvents => Set<PlatformAuditEventRecord>();

    internal DbSet<PlatformModuleStateChangeRecord> ModuleStateChanges => Set<PlatformModuleStateChangeRecord>();

    internal DbSet<PlatformModuleStateRecord> ModuleStates => Set<PlatformModuleStateRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(PlatformPersistenceDefaults.SchemaName);

        modelBuilder.Entity<PlatformModuleStateRecord>(builder =>
        {
            builder.ToTable(PlatformPersistenceDefaults.ModuleStatesTableName);
            builder.HasKey(record => record.ModuleKey);
            builder.Property(record => record.ModuleKey).HasMaxLength(128);
            builder.Property(record => record.DesiredState).HasConversion<string>().HasMaxLength(32);
            builder.Property(record => record.RuntimeState).HasConversion<string>().HasMaxLength(32);
            builder.Property(record => record.Version).IsConcurrencyToken();
            builder.Property(record => record.UpdatedUtc).HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedByActorId).HasMaxLength(256);
            builder.Property(record => record.LastErrorCode).HasMaxLength(128);
            builder.Property(record => record.LastErrorDetail).HasMaxLength(1024);
        });

        modelBuilder.Entity<PlatformModuleStateChangeRecord>(builder =>
        {
            builder.ToTable(PlatformPersistenceDefaults.ModuleStateChangesTableName);
            builder.HasKey(record => record.Id);
            builder.Property(record => record.ModuleKey).HasMaxLength(128);
            builder.Property(record => record.BeforeState).HasConversion<string>().HasMaxLength(32);
            builder.Property(record => record.AfterState).HasConversion<string>().HasMaxLength(32);
            builder.Property(record => record.ChangedUtc).HasConversion(InstantValueConverter);
            builder.Property(record => record.ActorId).HasMaxLength(256);
            builder.Property(record => record.TransitionId);
            builder.HasIndex(record => new { record.ModuleKey, record.ChangedUtc });
        });

        modelBuilder.Entity<PlatformAuditEventRecord>(builder =>
        {
            builder.ToTable(PlatformPersistenceDefaults.AuditEventsTableName);
            builder.HasKey(record => record.Id);
            builder.Property(record => record.ModuleKey).HasMaxLength(128);
            builder.Property(record => record.Action).HasMaxLength(128);
            builder.Property(record => record.TargetType).HasMaxLength(128);
            builder.Property(record => record.TargetId).HasMaxLength(256);
            builder.Property(record => record.Outcome).HasMaxLength(128);
            builder.Property(record => record.OccurredUtc).HasConversion(InstantValueConverter);
            builder.Property(record => record.ActorId).HasMaxLength(256);
            builder.Property(record => record.CorrelationId).HasMaxLength(256);
            builder.HasIndex(record => record.OccurredUtc);
        });
    }
}

internal sealed class PlatformModuleStateRecord
{
    public string ModuleKey { get; set; } = string.Empty;

    public ModuleDesiredState DesiredState { get; set; }

    public ModuleRuntimeState RuntimeState { get; set; }

    public long Version { get; set; }

    public Guid? TransitionId { get; set; }

    public Instant UpdatedUtc { get; set; }

    public string? UpdatedByActorId { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorDetail { get; set; }
}

internal sealed class PlatformModuleStateChangeRecord
{
    public long Id { get; set; }

    public string ModuleKey { get; set; } = string.Empty;

    public ModuleRuntimeState BeforeState { get; set; }

    public ModuleRuntimeState AfterState { get; set; }

    public Instant ChangedUtc { get; set; }

    public string? ActorId { get; set; }

    public Guid? TransitionId { get; set; }
}

internal sealed class PlatformAuditEventRecord
{
    public long Id { get; set; }

    public string ModuleKey { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public Instant OccurredUtc { get; set; }

    public string? ActorId { get; set; }

    public string? CorrelationId { get; set; }
}
