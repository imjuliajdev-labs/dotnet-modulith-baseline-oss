using Blog.Domain.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace Blog.Infrastructure.Persistence;

public sealed class BlogPersistenceDbContext : DbContext
{
    private static readonly ValueConverter<Instant, DateTimeOffset> InstantValueConverter = new(
        static instant => instant.ToDateTimeOffset(),
        static dateTimeOffset => Instant.FromDateTimeOffset(dateTimeOffset));

    private static readonly ValueConverter<Instant?, DateTimeOffset?> NullableInstantValueConverter = new(
        static instant => instant.HasValue ? instant.Value.ToDateTimeOffset() : null,
        static dateTimeOffset => dateTimeOffset.HasValue ? Instant.FromDateTimeOffset(dateTimeOffset.Value) : null);

    private static readonly ValueConverter<LocalDate?, DateOnly?> NullableLocalDateValueConverter = new(
        static localDate => localDate.HasValue ? new DateOnly(localDate.Value.Year, localDate.Value.Month, localDate.Value.Day) : null,
        static dateOnly => dateOnly.HasValue ? new LocalDate(dateOnly.Value.Year, dateOnly.Value.Month, dateOnly.Value.Day) : null);

    private static readonly ValueConverter<LocalTime?, TimeOnly?> NullableLocalTimeValueConverter = new(
        static localTime => localTime.HasValue
            ? new TimeOnly(localTime.Value.Hour, localTime.Value.Minute, localTime.Value.Second, localTime.Value.Millisecond)
            : null,
        static timeOnly => timeOnly.HasValue
            ? new LocalTime(timeOnly.Value.Hour, timeOnly.Value.Minute, timeOnly.Value.Second, timeOnly.Value.Millisecond)
            : null);

    public BlogPersistenceDbContext(DbContextOptions<BlogPersistenceDbContext> options)
        : base(options)
    {
    }

    internal DbSet<BlogPostRecord> Posts => Set<BlogPostRecord>();

    internal DbSet<BlogPostRevisionRecord> PostRevisions => Set<BlogPostRevisionRecord>();

    internal DbSet<BlogCategoryRecord> Categories => Set<BlogCategoryRecord>();

    internal DbSet<BlogTagRecord> Tags => Set<BlogTagRecord>();

    internal DbSet<BlogPostScheduleRecord> PostSchedules => Set<BlogPostScheduleRecord>();

    internal DbSet<BlogSettingsRecord> Settings => Set<BlogSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(BlogPersistenceDefaults.SchemaName);

        modelBuilder.ApplyConfiguration(new BlogPostEntityTypeConfiguration());

        modelBuilder.Entity<BlogCategoryRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.CategoriesTableName);
            builder.HasKey(record => record.Slug);
            builder.Property(record => record.Slug).HasColumnName("slug").HasMaxLength(128);
            builder.Property(record => record.Name).HasColumnName("name").HasMaxLength(256);
            builder.Property(record => record.Description).HasColumnName("description").HasMaxLength(1024);
            builder.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken();
            builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedByActorId).HasColumnName("updated_by_actor_id").HasMaxLength(256);
            builder.HasIndex(record => record.Name).HasDatabaseName("ix_blog_categories_name");
        });

        modelBuilder.Entity<BlogTagRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.TagsTableName);
            builder.HasKey(record => record.Slug);
            builder.Property(record => record.Slug).HasColumnName("slug").HasMaxLength(128);
            builder.Property(record => record.DisplayName).HasColumnName("display_name").HasMaxLength(256);
            builder.Property(record => record.Description).HasColumnName("description").HasMaxLength(1024);
            builder.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken();
            builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedByActorId).HasColumnName("updated_by_actor_id").HasMaxLength(256);
            builder.HasIndex(record => record.DisplayName).HasDatabaseName("ix_blog_tags_display_name");
        });

        modelBuilder.Entity<BlogPostTagRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.PostTagsTableName);
            builder.HasKey(record => new { record.PostId, record.TagSlug });
            builder.Property(record => record.PostId).HasColumnName("post_id");
            builder.Property(record => record.TagSlug).HasColumnName("tag_slug").HasMaxLength(128);
            builder.HasIndex(record => record.TagSlug).HasDatabaseName("ix_blog_post_tags_tag_slug");
        });

        modelBuilder.Entity<BlogPostShareTargetRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.PostShareTargetsTableName);
            builder.HasKey(record => new { record.PostId, record.Target });
            builder.Property(record => record.PostId).HasColumnName("post_id");
            builder.Property(record => record.Target).HasColumnName("target").HasMaxLength(64);
        });

        modelBuilder.Entity<BlogPostRevisionRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.PostRevisionsTableName);
            builder.HasKey(record => record.Id);
            builder.Property(record => record.Id).HasColumnName("id");
            builder.Property(record => record.PostId).HasColumnName("post_id");
            builder.Property(record => record.RevisionNumber).HasColumnName("revision_number");
            builder.Property(record => record.PostVersion).HasColumnName("post_version");
            builder.Property(record => record.Title).HasColumnName("title").HasMaxLength(256);
            builder.Property(record => record.Summary).HasColumnName("summary").HasMaxLength(1024);
            builder.Property(record => record.Body).HasColumnName("body").HasColumnType("text");
            builder.Property(record => record.Featured).HasColumnName("featured");
            builder.Property(record => record.CategorySlug).HasColumnName("category_slug").HasMaxLength(128);
            builder.Property(record => record.TagNamesJson).HasColumnName("tag_names_json").HasColumnType("text");
            builder.Property(record => record.SeoTitle).HasColumnName("seo_title").HasMaxLength(256);
            builder.Property(record => record.SeoDescription).HasColumnName("seo_description").HasMaxLength(512);
            builder.Property(record => record.SeoKeywords).HasColumnName("seo_keywords").HasMaxLength(512);
            builder.Property(record => record.ShareTargetsJson).HasColumnName("share_targets_json").HasColumnType("text");
            builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(256);
            builder.HasIndex(record => new { record.PostId, record.RevisionNumber }).IsUnique().HasDatabaseName("ix_blog_post_revisions_post_id_revision_number");
        });

        modelBuilder.Entity<BlogPostScheduleRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.PostPublicationSchedulesTableName);
            builder.HasKey(record => record.PostId);
            builder.Property(record => record.PostId).HasColumnName("post_id");
            builder.Property(record => record.ScheduledPublishLocalDate).HasColumnName("scheduled_publish_local_date").HasConversion(NullableLocalDateValueConverter);
            builder.Property(record => record.ScheduledPublishLocalTime).HasColumnName("scheduled_publish_local_time").HasConversion(NullableLocalTimeValueConverter);
            builder.Property(record => record.ScheduledPublishTimeZoneId).HasColumnName("scheduled_publish_time_zone_id").HasMaxLength(128);
            builder.Property(record => record.ScheduledPublishLocalTimeResolution).HasColumnName("scheduled_publish_local_time_resolution").HasMaxLength(64);
            builder.Property(record => record.ScheduledPublishForUtc).HasColumnName("scheduled_publish_for_utc");
            builder.Property(record => record.ScheduledPublishByActorId).HasColumnName("scheduled_publish_by_actor_id").HasMaxLength(256);
            builder.Property(record => record.ScheduledUnpublishLocalDate).HasColumnName("scheduled_unpublish_local_date").HasConversion(NullableLocalDateValueConverter);
            builder.Property(record => record.ScheduledUnpublishLocalTime).HasColumnName("scheduled_unpublish_local_time").HasConversion(NullableLocalTimeValueConverter);
            builder.Property(record => record.ScheduledUnpublishTimeZoneId).HasColumnName("scheduled_unpublish_time_zone_id").HasMaxLength(128);
            builder.Property(record => record.ScheduledUnpublishLocalTimeResolution).HasColumnName("scheduled_unpublish_local_time_resolution").HasMaxLength(64);
            builder.Property(record => record.ScheduledUnpublishForUtc).HasColumnName("scheduled_unpublish_for_utc");
            builder.Property(record => record.ScheduledUnpublishByActorId).HasColumnName("scheduled_unpublish_by_actor_id").HasMaxLength(256);
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedByActorId).HasColumnName("updated_by_actor_id").HasMaxLength(256);
            builder.HasIndex(record => record.ScheduledPublishForUtc).HasDatabaseName("ix_blog_post_publication_schedules_publish_utc");
            builder.HasIndex(record => record.ScheduledUnpublishForUtc).HasDatabaseName("ix_blog_post_publication_schedules_unpublish_utc");
        });

        modelBuilder.Entity<BlogSettingsRecord>(builder =>
        {
            builder.ToTable(BlogPersistenceDefaults.SettingsTableName);
            builder.HasKey(record => record.SettingsKey);
            builder.Property(record => record.SettingsKey).HasColumnName("settings_key").HasMaxLength(64);
            builder.Property(record => record.OperatorSummary).HasColumnName("operator_summary").HasMaxLength(1024);
            builder.Property(record => record.PreviewLimit).HasColumnName("preview_limit");
            builder.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken();
            builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
            builder.Property(record => record.UpdatedByActorId).HasColumnName("updated_by_actor_id").HasMaxLength(256);
        });
    }
}

internal sealed class BlogPostRecord
{
    public Guid PostId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool Featured { get; set; }

    public BlogPostStatus Status { get; set; }

    public int Version { get; set; }

    public int RevisionNumber { get; set; }

    public string? CategorySlug { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public string? SeoKeywords { get; set; }

    public long ViewCount { get; set; }

    public Instant CreatedUtc { get; set; }

    public Instant UpdatedUtc { get; set; }

    public string UpdatedByActorId { get; set; } = string.Empty;

    public Instant? PublishedUtc { get; set; }

    public string? PublishedByActorId { get; set; }

    public List<BlogPostTagRecord> Tags { get; set; } = [];

    public List<BlogPostShareTargetRecord> ShareTargets { get; set; } = [];

    public List<BlogPostRevisionRecord> Revisions { get; set; } = [];

    public BlogPostScheduleRecord? PublicationSchedule { get; set; }
}

internal sealed class BlogCategoryRecord
{
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Version { get; set; }

    public Instant CreatedUtc { get; set; }

    public Instant UpdatedUtc { get; set; }

    public string UpdatedByActorId { get; set; } = string.Empty;
}

internal sealed class BlogTagRecord
{
    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Version { get; set; }

    public Instant CreatedUtc { get; set; }

    public Instant UpdatedUtc { get; set; }

    public string UpdatedByActorId { get; set; } = string.Empty;
}

internal sealed class BlogPostTagRecord
{
    public Guid PostId { get; set; }

    public string TagSlug { get; set; } = string.Empty;

    public BlogPostRecord Post { get; set; } = null!;
}

internal sealed class BlogPostShareTargetRecord
{
    public Guid PostId { get; set; }

    public string Target { get; set; } = string.Empty;

    public BlogPostRecord Post { get; set; } = null!;
}

internal sealed class BlogPostRevisionRecord
{
    public long Id { get; set; }

    public Guid PostId { get; set; }

    public int RevisionNumber { get; set; }

    public int PostVersion { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool Featured { get; set; }

    public string? CategorySlug { get; set; }

    public string TagNamesJson { get; set; } = "[]";

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public string? SeoKeywords { get; set; }

    public string ShareTargetsJson { get; set; } = "[]";

    public Instant CreatedUtc { get; set; }

    public string CreatedByActorId { get; set; } = string.Empty;

    public BlogPostRecord Post { get; set; } = null!;
}

internal sealed class BlogPostScheduleRecord
{
    public Guid PostId { get; set; }

    public LocalDate? ScheduledPublishLocalDate { get; set; }

    public LocalTime? ScheduledPublishLocalTime { get; set; }

    public string? ScheduledPublishTimeZoneId { get; set; }

    public string? ScheduledPublishLocalTimeResolution { get; set; }

    public DateTimeOffset? ScheduledPublishForUtc { get; set; }

    public string? ScheduledPublishByActorId { get; set; }

    public LocalDate? ScheduledUnpublishLocalDate { get; set; }

    public LocalTime? ScheduledUnpublishLocalTime { get; set; }

    public string? ScheduledUnpublishTimeZoneId { get; set; }

    public string? ScheduledUnpublishLocalTimeResolution { get; set; }

    public DateTimeOffset? ScheduledUnpublishForUtc { get; set; }

    public string? ScheduledUnpublishByActorId { get; set; }

    public Instant UpdatedUtc { get; set; }

    public string UpdatedByActorId { get; set; } = string.Empty;

    public BlogPostRecord Post { get; set; } = null!;
}

internal sealed class BlogSettingsRecord
{
    public string SettingsKey { get; set; } = string.Empty;

    public string OperatorSummary { get; set; } = string.Empty;

    public int PreviewLimit { get; set; }

    public int Version { get; set; }

    public Instant UpdatedUtc { get; set; }

    public string UpdatedByActorId { get; set; } = string.Empty;
}
