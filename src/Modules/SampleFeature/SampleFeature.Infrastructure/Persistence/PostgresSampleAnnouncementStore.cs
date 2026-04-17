using Microsoft.EntityFrameworkCore;
using NodaTime;
using SampleFeature.Application.Publishing;

namespace SampleFeature.Infrastructure.Persistence;

internal sealed class PostgresSampleAnnouncementStore : ISampleAnnouncementWriter
{
    private readonly IDbContextFactory<SampleFeaturePersistenceDbContext> _dbContextFactory;

    public PostgresSampleAnnouncementStore(IDbContextFactory<SampleFeaturePersistenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async ValueTask WriteAsync(PublishedSampleAnnouncement announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Announcements.Add(new SampleAnnouncementRecord
        {
            AnnouncementId = announcement.AnnouncementId,
            Title = announcement.Title,
            Body = announcement.Body,
            PublishedUtc = announcement.PublishedAt,
            PublishedByActorId = announcement.PublishedByActorId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
