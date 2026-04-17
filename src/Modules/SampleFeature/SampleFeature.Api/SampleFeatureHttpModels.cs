using NodaTime;
using NodaTime.Text;
using BuildingBlocks.Application.Results;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Scheduling;

namespace SampleFeature.Api;

public sealed record PublishSampleAnnouncementRequest(string Title, string Body);

public sealed record ScheduleSampleAnnouncementRequest(string Title, string Body, string ScheduledLocalDate, string ScheduledLocalTime)
{
    private static readonly Error InvalidScheduledLocalDate = new(
        "sample-feature.invalid_scheduled_local_date",
        "Scheduled local date must use the ISO format yyyy-MM-dd.",
        ErrorKind.Validation);

    private static readonly Error InvalidScheduledLocalTime = new(
        "sample-feature.invalid_scheduled_local_time",
        "Scheduled local time must use the 24-hour format HH:mm.",
        ErrorKind.Validation);

    public Result<ScheduleSampleAnnouncementCommand> ToCommand(string requestKey)
    {
        var parsedDate = LocalDatePattern.Iso.Parse(ScheduledLocalDate ?? string.Empty);
        if (!parsedDate.Success)
        {
            return Result<ScheduleSampleAnnouncementCommand>.Failure(InvalidScheduledLocalDate);
        }

        var timePattern = LocalTimePattern.ExtendedIso;
        var parsedTime = timePattern.Parse(ScheduledLocalTime ?? string.Empty);
        if (!parsedTime.Success)
        {
            return Result<ScheduleSampleAnnouncementCommand>.Failure(InvalidScheduledLocalTime);
        }

        return Result<ScheduleSampleAnnouncementCommand>.Success(new ScheduleSampleAnnouncementCommand(
            requestKey,
            Title,
            Body,
            parsedDate.Value,
            parsedTime.Value));
    }
}

public sealed record PublishedSampleAnnouncementResponse(
    Guid AnnouncementId,
    string Title,
    string Body,
    Instant PublishedAt,
    string PublishedByActorId)
{
    public static PublishedSampleAnnouncementResponse From(PublishedSampleAnnouncement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        return new PublishedSampleAnnouncementResponse(
            announcement.AnnouncementId,
            announcement.Title,
            announcement.Body,
            announcement.PublishedAt,
            announcement.PublishedByActorId);
    }
}

public sealed record ScheduledSampleAnnouncementResponse(
    Guid ScheduledAnnouncementId,
    string Title,
    string Body,
    DateOnly ScheduledLocalDate,
    TimeOnly ScheduledLocalTime,
    string TimeZoneId,
    DateTimeOffset ScheduledForUtc,
    string ScheduledByActorId,
    string LocalTimeResolution,
    string Status)
{
    public Guid? PublishedAnnouncementId { get; init; }

    public DateTimeOffset? PublishedUtc { get; init; }

    public static ScheduledSampleAnnouncementResponse From(ScheduledSampleAnnouncement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        return new ScheduledSampleAnnouncementResponse(
            announcement.ScheduledAnnouncementId,
            announcement.Title,
            announcement.Body,
            new DateOnly(announcement.ScheduledLocalDate.Year, announcement.ScheduledLocalDate.Month, announcement.ScheduledLocalDate.Day),
            new TimeOnly(announcement.ScheduledLocalTime.Hour, announcement.ScheduledLocalTime.Minute, announcement.ScheduledLocalTime.Second, announcement.ScheduledLocalTime.Millisecond),
            announcement.TimeZoneId,
            announcement.ScheduledForUtc.ToDateTimeOffset(),
            announcement.ScheduledByActorId,
            announcement.LocalTimeResolution,
            announcement.Status)
        {
            PublishedAnnouncementId = announcement.PublishedAnnouncementId,
            PublishedUtc = announcement.PublishedUtc?.ToDateTimeOffset()
        };
    }
}

public sealed record ScheduledSampleAnnouncementListResponse(IReadOnlyCollection<ScheduledSampleAnnouncementResponse> Announcements)
{
    public static ScheduledSampleAnnouncementListResponse From(ScheduledSampleAnnouncementList announcements)
    {
        ArgumentNullException.ThrowIfNull(announcements);

        return new ScheduledSampleAnnouncementListResponse(
            announcements.Announcements.Select(ScheduledSampleAnnouncementResponse.From).ToArray());
    }
}
