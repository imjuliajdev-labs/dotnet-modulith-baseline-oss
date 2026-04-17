using Blog.Domain.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace Blog.Infrastructure.Persistence;

internal sealed class BlogPostEntityTypeConfiguration : IEntityTypeConfiguration<BlogPostRecord>
{
    private static readonly ValueConverter<Instant, DateTimeOffset> InstantValueConverter = new(
        static instant => instant.ToDateTimeOffset(),
        static dateTimeOffset => Instant.FromDateTimeOffset(dateTimeOffset));

    private static readonly ValueConverter<Instant?, DateTimeOffset?> NullableInstantValueConverter = new(
        static instant => instant.HasValue ? instant.Value.ToDateTimeOffset() : null,
        static dateTimeOffset => dateTimeOffset.HasValue ? Instant.FromDateTimeOffset(dateTimeOffset.Value) : null);

    public void Configure(EntityTypeBuilder<BlogPostRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(BlogPersistenceDefaults.PostsTableName);
        builder.HasKey(record => record.PostId);
        builder.Property(record => record.PostId).HasColumnName("post_id");
        builder.Property(record => record.Slug).HasColumnName("slug").HasMaxLength(200);
        builder.Property(record => record.Title).HasColumnName("title").HasMaxLength(256);
        builder.Property(record => record.Summary).HasColumnName("summary").HasMaxLength(1024);
        builder.Property(record => record.Body).HasColumnName("body").HasColumnType("text");
        builder.Property(record => record.Featured).HasColumnName("featured");
        builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(record => record.RevisionNumber).HasColumnName("revision_number");
        builder.Property(record => record.CategorySlug).HasColumnName("category_slug").HasMaxLength(128);
        builder.Property(record => record.SeoTitle).HasColumnName("seo_title").HasMaxLength(256);
        builder.Property(record => record.SeoDescription).HasColumnName("seo_description").HasMaxLength(512);
        builder.Property(record => record.SeoKeywords).HasColumnName("seo_keywords").HasMaxLength(512);
        builder.Property(record => record.ViewCount).HasColumnName("view_count");
        builder.Property(record => record.CreatedUtc).HasColumnName("created_utc").HasConversion(InstantValueConverter);
        builder.Property(record => record.UpdatedUtc).HasColumnName("updated_utc").HasConversion(InstantValueConverter);
        builder.Property(record => record.UpdatedByActorId).HasColumnName("updated_by_actor_id").HasMaxLength(256);
        builder.Property(record => record.PublishedUtc).HasColumnName("published_utc").HasConversion(NullableInstantValueConverter);
        builder.Property(record => record.PublishedByActorId).HasColumnName("published_by_actor_id").HasMaxLength(256);
        builder.HasIndex(record => record.Slug).IsUnique().HasDatabaseName("ux_blog_posts_slug");
        builder.HasIndex(record => new { record.Status, record.PublishedUtc }).HasDatabaseName("ix_blog_posts_status_published_utc");
        builder.HasMany(record => record.Tags)
            .WithOne(record => record.Post)
            .HasForeignKey(record => record.PostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(record => record.ShareTargets)
            .WithOne(record => record.Post)
            .HasForeignKey(record => record.PostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(record => record.Revisions)
            .WithOne(record => record.Post)
            .HasForeignKey(record => record.PostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(record => record.PublicationSchedule)
            .WithOne(record => record.Post)
            .HasForeignKey<BlogPostScheduleRecord>(record => record.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
