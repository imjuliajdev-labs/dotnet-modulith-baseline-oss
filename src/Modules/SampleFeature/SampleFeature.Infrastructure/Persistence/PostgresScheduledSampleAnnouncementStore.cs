using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using SampleFeature.Application.Scheduling;

namespace SampleFeature.Infrastructure.Persistence;

internal sealed class PostgresScheduledSampleAnnouncementStore : IScheduledSampleAnnouncementStore
{
    private readonly IDbContextFactory<SampleFeaturePersistenceDbContext> _dbContextFactory;

    public PostgresScheduledSampleAnnouncementStore(IDbContextFactory<SampleFeaturePersistenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async ValueTask ScheduleAsync(ScheduledSampleAnnouncement announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ScheduledAnnouncements.Add(MapToRecord(announcement));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ScheduledSampleAnnouncement>> ListForActorAsync(string actorId, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var records = await dbContext.ScheduledAnnouncements
            .AsNoTracking()
            .Where(record => record.ScheduledByActorId == actorId)
            .OrderByDescending(record => record.ScheduledForUtc)
            .ThenByDescending(record => record.CreatedUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return records.Select(MapFromRecord).ToArray();
    }

    public async ValueTask<IReadOnlyCollection<LeasedScheduledSampleAnnouncement>> LeaseDueAsync(
        Instant now,
        Guid leaseId,
        Instant leaseUntil,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        var sql = $"""
            WITH due AS (
                SELECT scheduled_announcement_id
                FROM {SampleFeaturePersistenceDefaults.SchemaName}.{SampleFeaturePersistenceDefaults.ScheduledAnnouncementsTableName}
                WHERE status = @pendingStatus
                    AND scheduled_for_utc <= @now
                    AND (lease_until_utc IS NULL OR lease_until_utc < @now)
                ORDER BY scheduled_for_utc, created_utc
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED)
            UPDATE {SampleFeaturePersistenceDefaults.SchemaName}.{SampleFeaturePersistenceDefaults.ScheduledAnnouncementsTableName} AS target
            SET lease_id = @leaseId,
                lease_until_utc = @leaseUntilUtc,
                updated_utc = @now
            FROM due
            WHERE target.scheduled_announcement_id = due.scheduled_announcement_id
            RETURNING target.scheduled_announcement_id, target.title, target.body, target.scheduled_local_date, target.scheduled_local_time,
                      target.time_zone_id, target.scheduled_for_utc, target.scheduled_by_actor_id, target.local_time_resolution,
                      target.created_utc, target.updated_utc;
            """;

        var announcements = new List<LeasedScheduledSampleAnnouncement>();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("pendingStatus", SampleAnnouncementSchedulingStatuses.Pending);
        command.Parameters.AddWithValue("now", now.ToDateTimeOffset());
        command.Parameters.AddWithValue("leaseId", leaseId);
        command.Parameters.AddWithValue("leaseUntilUtc", leaseUntil.ToDateTimeOffset());
        command.Parameters.AddWithValue("batchSize", batchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            announcements.Add(new LeasedScheduledSampleAnnouncement(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadLocalDate(reader, 3),
                ReadLocalTime(reader, 4),
                reader.GetString(5),
                Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(6)),
                reader.GetString(7),
                reader.GetString(8),
                leaseId,
                Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(9)),
                Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(10))));
        }

        return announcements;
    }

    public async ValueTask MarkPublishedAsync(
        Guid scheduledAnnouncementId,
        Guid leaseId,
        Guid publishedAnnouncementId,
        Instant publishedUtc,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.ScheduledAnnouncements
            .Where(record => record.ScheduledAnnouncementId == scheduledAnnouncementId && record.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.Status, SampleAnnouncementSchedulingStatuses.Published)
                .SetProperty(record => record.PublishedAnnouncementId, publishedAnnouncementId)
                .SetProperty(record => record.PublishedUtc, publishedUtc)
                .SetProperty(record => record.LeaseId, (Guid?)null)
                .SetProperty(record => record.LeaseUntilUtc, (Instant?)null)
                .SetProperty(record => record.UpdatedUtc, publishedUtc),
                cancellationToken);
    }

    private static ScheduledSampleAnnouncementRecord MapToRecord(ScheduledSampleAnnouncement announcement)
    {
        return new ScheduledSampleAnnouncementRecord
        {
            ScheduledAnnouncementId = announcement.ScheduledAnnouncementId,
            Title = announcement.Title,
            Body = announcement.Body,
            ScheduledLocalDate = announcement.ScheduledLocalDate,
            ScheduledLocalTime = announcement.ScheduledLocalTime,
            TimeZoneId = announcement.TimeZoneId,
            ScheduledForUtc = announcement.ScheduledForUtc,
            ScheduledByActorId = announcement.ScheduledByActorId,
            LocalTimeResolution = announcement.LocalTimeResolution,
            Status = announcement.Status,
            CreatedUtc = announcement.CreatedUtc,
            UpdatedUtc = announcement.UpdatedUtc,
            PublishedAnnouncementId = announcement.PublishedAnnouncementId,
            PublishedUtc = announcement.PublishedUtc
        };
    }

    private static ScheduledSampleAnnouncement MapFromRecord(ScheduledSampleAnnouncementRecord record)
    {
        return new ScheduledSampleAnnouncement(
            record.ScheduledAnnouncementId,
            record.Title,
            record.Body,
            record.ScheduledLocalDate,
            record.ScheduledLocalTime,
            record.TimeZoneId,
            record.ScheduledForUtc,
            record.ScheduledByActorId,
            record.LocalTimeResolution,
            record.Status,
            record.CreatedUtc,
            record.UpdatedUtc)
        {
            PublishedAnnouncementId = record.PublishedAnnouncementId,
            PublishedUtc = record.PublishedUtc
        };
    }

    private static LocalDate ReadLocalDate(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetFieldValue<DateOnly>(ordinal);
        return new LocalDate(value.Year, value.Month, value.Day);
    }

    private static LocalTime ReadLocalTime(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetFieldValue<TimeOnly>(ordinal);
        return new LocalTime(value.Hour, value.Minute, value.Second, value.Millisecond);
    }
}
