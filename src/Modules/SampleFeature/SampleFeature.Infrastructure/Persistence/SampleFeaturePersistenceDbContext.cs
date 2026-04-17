using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace SampleFeature.Infrastructure.Persistence;

public sealed class SampleFeaturePersistenceDbContext : DbContext
{
    private static readonly ValueConverter<Instant, DateTimeOffset> InstantValueConverter = new(
        static instant => instant.ToDateTimeOffset(),
        static dateTimeOffset => Instant.FromDateTimeOffset(dateTimeOffset));

    private static readonly ValueConverter<Instant?, DateTimeOffset?> NullableInstantValueConverter = new(
        static instant => instant.HasValue ? instant.Value.ToDateTimeOffset() : null,
        static dateTimeOffset => dateTimeOffset.HasValue ? Instant.FromDateTimeOffset(dateTimeOffset.Value) : null);

    private static readonly ValueConverter<LocalDate, DateOnly> LocalDateValueConverter = new(
        static localDate => new DateOnly(localDate.Year, localDate.Month, localDate.Day),
        static dateOnly => new LocalDate(dateOnly.Year, dateOnly.Month, dateOnly.Day));

    private static readonly ValueConverter<LocalTime, TimeOnly> LocalTimeValueConverter = new(
        static localTime => new TimeOnly(localTime.Hour, localTime.Minute, localTime.Second, localTime.Millisecond),
        static timeOnly => new LocalTime(timeOnly.Hour, timeOnly.Minute, timeOnly.Second, timeOnly.Millisecond));

    public SampleFeaturePersistenceDbContext(DbContextOptions<SampleFeaturePersistenceDbContext> options)
        : base(options)
    {
    }

    internal DbSet<SampleAnnouncementRecord> Announcements => Set<SampleAnnouncementRecord>();

    internal DbSet<ScheduledSampleAnnouncementRecord> ScheduledAnnouncements => Set<ScheduledSampleAnnouncementRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SampleFeaturePersistenceDefaults.SchemaName);

        modelBuilder.ApplyConfiguration(new SampleFeatureAnnouncementEntityTypeConfiguration());

        modelBuilder.Entity<ScheduledSampleAnnouncementRecord>(builder =>
        {
            builder.ToTable(SampleFeaturePersistenceDefaults.ScheduledAnnouncementsTableName);
            builder.HasKey(record => record.ScheduledAnnouncementId);
            builder.Property(record => record.ScheduledAnnouncementId).HasColumnName("scheduled_announcement_id");
            builder.Property(record => record.Title).HasColumnName("title").HasColumnType("text");
            builder.Property(record => record.Body).HasColumnName("body").HasColumnType("text");
            builder.Property(record => record.ScheduledLocalDate).HasColumnName("scheduled_local_date").HasConversion(LocalDateValueConverter);
            builder.Property(record => record.ScheduledLocalTime).HasColumnName("scheduled_local_time").HasConversion(LocalTimeValueConverter);
            builder.Property(record => record.TimeZoneId).HasColumnName("time_zone_id").HasColumnType("text");
            builder.Property(record => record.ScheduledForUtc).HasColumnName("scheduled_for_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.ScheduledByActorId).HasColumnName("scheduled_by_actor_id").HasColumnType("text");
            builder.Property(record => record.LocalTimeResolution).HasColumnName("local_time_resolution").HasColumnType("text");
            builder.Property(record => record.Status).HasColumnName("status").HasColumnType("text");
            builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.PublishedAnnouncementId).HasColumnName("published_announcement_id");
            builder.Property(record => record.PublishedUtc).HasColumnName("published_utc").HasConversion(NullableInstantValueConverter);
            builder.Property(record => record.LeaseId).HasColumnName("lease_id");
            builder.Property(record => record.LeaseUntilUtc).HasColumnName("lease_until_utc").HasConversion(NullableInstantValueConverter);
            builder.HasIndex(record => new { record.Status, record.ScheduledForUtc })
                .HasDatabaseName("ix_sample_feature_scheduled_announcements_due");
            builder.HasIndex(record => new { record.ScheduledByActorId, record.ScheduledForUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_sample_feature_scheduled_announcements_actor");
        });
    }
}

internal sealed class SampleAnnouncementRecord
{
    public Guid AnnouncementId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public Instant PublishedUtc { get; set; }

    public string PublishedByActorId { get; set; } = string.Empty;
}

internal sealed class ScheduledSampleAnnouncementRecord
{
    public Guid ScheduledAnnouncementId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public LocalDate ScheduledLocalDate { get; set; }

    public LocalTime ScheduledLocalTime { get; set; }

    public string TimeZoneId { get; set; } = string.Empty;

    public Instant ScheduledForUtc { get; set; }

    public string ScheduledByActorId { get; set; } = string.Empty;

    public string LocalTimeResolution { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Instant CreatedUtc { get; set; }

    public Instant UpdatedUtc { get; set; }

    public Guid? PublishedAnnouncementId { get; set; }

    public Instant? PublishedUtc { get; set; }

    public Guid? LeaseId { get; set; }

    public Instant? LeaseUntilUtc { get; set; }
}
