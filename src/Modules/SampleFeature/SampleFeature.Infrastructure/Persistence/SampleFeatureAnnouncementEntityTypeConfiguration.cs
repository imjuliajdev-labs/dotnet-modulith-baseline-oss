using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace SampleFeature.Infrastructure.Persistence;

internal sealed class SampleFeatureAnnouncementEntityTypeConfiguration : IEntityTypeConfiguration<SampleAnnouncementRecord>
{
    private static readonly ValueConverter<Instant, DateTimeOffset> InstantValueConverter = new(
        static instant => instant.ToDateTimeOffset(),
        static dateTimeOffset => Instant.FromDateTimeOffset(dateTimeOffset));

    public void Configure(EntityTypeBuilder<SampleAnnouncementRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(SampleFeaturePersistenceDefaults.AnnouncementsTableName);
        builder.HasKey(record => record.AnnouncementId);
        builder.Property(record => record.AnnouncementId).HasColumnName("announcement_id");
        builder.Property(record => record.Title).HasColumnName("title").HasColumnType("text");
        builder.Property(record => record.Body).HasColumnName("body").HasColumnType("text");
        builder.Property(record => record.PublishedUtc).HasColumnName("published_utc").HasConversion(InstantValueConverter);
        builder.Property(record => record.PublishedByActorId).HasColumnName("published_by_actor_id").HasColumnType("text");
    }
}
