using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using NodaTime;
using NodaTime.TimeZones;
using SampleFeature.Application.Authorization;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.SharedReads;
using SampleFeature.Domain.Announcements;

namespace SampleFeature.Application.Scheduling;

public static class SampleAnnouncementSchedulingDefaults
{
    public const int DefaultListLimit = 10;
    public const int MaxListLimit = 25;
    public const int DefaultProcessingBatchSize = 10;
    public static readonly Duration ProcessingLeaseDuration = Duration.FromMinutes(5);
}

public static class SampleAnnouncementSchedulingStatuses
{
    public const string Pending = "pending";
    public const string Published = "published";
}

public static class SampleAnnouncementLocalTimeResolutions
{
    public const string Exact = "exact";
    public const string SkippedForward = "skipped_forward";
    public const string AmbiguousEarlier = "ambiguous_earlier";
}

public sealed record ScheduledSampleAnnouncement(
    Guid ScheduledAnnouncementId,
    string Title,
    string Body,
    LocalDate ScheduledLocalDate,
    LocalTime ScheduledLocalTime,
    string TimeZoneId,
    Instant ScheduledForUtc,
    string ScheduledByActorId,
    string LocalTimeResolution,
    string Status,
    Instant CreatedUtc,
    Instant UpdatedUtc)
{
    public Guid? PublishedAnnouncementId { get; init; }

    public Instant? PublishedUtc { get; init; }
}

public sealed record ScheduledSampleAnnouncementList(IReadOnlyCollection<ScheduledSampleAnnouncement> Announcements);

public sealed record LeasedScheduledSampleAnnouncement(
    Guid ScheduledAnnouncementId,
    string Title,
    string Body,
    LocalDate ScheduledLocalDate,
    LocalTime ScheduledLocalTime,
    string TimeZoneId,
    Instant ScheduledForUtc,
    string ScheduledByActorId,
    string LocalTimeResolution,
    Guid LeaseId,
    Instant CreatedUtc,
    Instant UpdatedUtc);

public interface IScheduledSampleAnnouncementStore
{
    ValueTask ScheduleAsync(ScheduledSampleAnnouncement announcement, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<ScheduledSampleAnnouncement>> ListForActorAsync(string actorId, int limit, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<LeasedScheduledSampleAnnouncement>> LeaseDueAsync(
        Instant now,
        Guid leaseId,
        Instant leaseUntil,
        int batchSize,
        CancellationToken cancellationToken);

    ValueTask MarkPublishedAsync(
        Guid scheduledAnnouncementId,
        Guid leaseId,
        Guid publishedAnnouncementId,
        Instant publishedUtc,
        CancellationToken cancellationToken);
}

public sealed record ScheduleSampleAnnouncementCommand(
    string RequestKey,
    string Title,
    string Body,
    LocalDate ScheduledLocalDate,
    LocalTime ScheduledLocalTime)
    : IIdempotentCommand<ScheduledSampleAnnouncement>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => SampleFeatureModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

public sealed record ListScheduledSampleAnnouncementsQuery(int Limit = SampleAnnouncementSchedulingDefaults.DefaultListLimit)
    : IQuery<ScheduledSampleAnnouncementList>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => SampleFeatureModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

public sealed record ProcessDueScheduledSampleAnnouncementsCommand(int BatchSize = SampleAnnouncementSchedulingDefaults.DefaultProcessingBatchSize)
    : ICommand<int>, IModuleScoped
{
    public string ModuleKey => SampleFeatureModuleInfo.ModuleKey;
}

public static class SampleAnnouncementSchedulingErrors
{
    public static Error BodyRequired()
    {
        return new Error(
            "sample-feature.scheduled_announcement_body_required",
            "Scheduled announcement body is required.",
            ErrorKind.Validation);
    }

    public static Error CurrentActorRequired()
    {
        return new Error(
            "sample-feature.scheduled_announcement_current_actor_required",
            "A signed-in actor is required to schedule announcements.",
            ErrorKind.Unauthorized);
    }

    public static Error CurrentActorTimeZoneInvalid(string timeZoneId)
    {
        return new Error(
            "sample-feature.scheduled_announcement_time_zone_invalid",
            $"The current actor preferred time zone '{timeZoneId}' is not a valid IANA time zone id.",
            ErrorKind.Validation);
    }

    public static Error CurrentActorTimeZoneRequired()
    {
        return new Error(
            "sample-feature.scheduled_announcement_time_zone_required",
            "The current actor does not have a preferred time zone configured.",
            ErrorKind.Validation);
    }

    public static Error CurrentActorTimeZoneUnavailable()
    {
        return new Error(
            "sample-feature.scheduled_announcement_time_zone_unavailable",
            "The current actor preferred time zone could not be confirmed within the bounded shared-read policy.",
            ErrorKind.ServiceUnavailable);
    }

    public static Error TitleRequired()
    {
        return new Error(
            "sample-feature.scheduled_announcement_title_required",
            "Scheduled announcement title is required.",
            ErrorKind.Validation);
    }
}

internal sealed class ScheduleSampleAnnouncementCommandHandler : ICommandHandler<ScheduleSampleAnnouncementCommand, ScheduledSampleAnnouncement>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly ISampleFeatureIdentityTimeZoneReader _timeZoneReader;
    private readonly IScheduledSampleAnnouncementStore _store;

    public ScheduleSampleAnnouncementCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        ISampleFeatureIdentityTimeZoneReader timeZoneReader,
        IScheduledSampleAnnouncementStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _timeZoneReader = timeZoneReader ?? throw new ArgumentNullException(nameof(timeZoneReader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<ScheduledSampleAnnouncement>> Handle(ScheduleSampleAnnouncementCommand command, CancellationToken cancellationToken)
    {
        if (!SampleAnnouncementContent.TryCreate(command.Title, command.Body, out var content, out var validationError))
        {
            return Result<ScheduledSampleAnnouncement>.Failure(MapValidationError(validationError));
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.ActorId))
        {
            return Result<ScheduledSampleAnnouncement>.Failure(SampleAnnouncementSchedulingErrors.CurrentActorRequired());
        }

        var timeZoneId = await _timeZoneReader.GetRequiredTimeZoneIdAsync(actor.ActorId, cancellationToken);
        if (timeZoneId.IsFailure)
        {
            return Result<ScheduledSampleAnnouncement>.Failure(timeZoneId.Error);
        }

        var resolution = SampleAnnouncementScheduleResolver.ResolveScheduledInstant(command.ScheduledLocalDate, command.ScheduledLocalTime, timeZoneId.Value!);
        var createdUtc = _clock.GetCurrentInstant();
        var scheduled = new ScheduledSampleAnnouncement(
            Guid.NewGuid(),
            content!.Title,
            content.Body,
            command.ScheduledLocalDate,
            command.ScheduledLocalTime,
            timeZoneId.Value!,
            resolution.ScheduledForUtc,
            actor.ActorId,
            resolution.LocalTimeResolution,
            SampleAnnouncementSchedulingStatuses.Pending,
            createdUtc,
            createdUtc);

        await _store.ScheduleAsync(scheduled, cancellationToken);

        await SampleAnnouncementAuditing.WriteAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actor.ActorId,
            createdUtc,
            SampleAnnouncementAuditing.ActionSchedule,
            SampleAnnouncementAuditing.TargetTypeScheduledAnnouncement,
            scheduled.ScheduledAnnouncementId.ToString(),
            SampleAnnouncementAuditing.OutcomeScheduled,
            cancellationToken);

        return Result<ScheduledSampleAnnouncement>.Success(scheduled);
    }

    private static Error MapValidationError(SampleAnnouncementContentValidationError validationError)
    {
        return validationError switch
        {
            SampleAnnouncementContentValidationError.TitleRequired => SampleAnnouncementSchedulingErrors.TitleRequired(),
            SampleAnnouncementContentValidationError.BodyRequired => SampleAnnouncementSchedulingErrors.BodyRequired(),
            _ => throw new InvalidOperationException($"Unsupported validation error '{validationError}'.")
        };
    }
}

internal sealed class ListScheduledSampleAnnouncementsQueryHandler : IQueryHandler<ListScheduledSampleAnnouncementsQuery, ScheduledSampleAnnouncementList>
{
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IScheduledSampleAnnouncementStore _store;

    public ListScheduledSampleAnnouncementsQueryHandler(ICurrentActorAccessor currentActorAccessor, IScheduledSampleAnnouncementStore store)
    {
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<ScheduledSampleAnnouncementList>> Handle(ListScheduledSampleAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.ActorId))
        {
            return Result<ScheduledSampleAnnouncementList>.Failure(SampleAnnouncementSchedulingErrors.CurrentActorRequired());
        }

        var normalizedLimit = Math.Clamp(query.Limit, 1, SampleAnnouncementSchedulingDefaults.MaxListLimit);
        var scheduled = await _store.ListForActorAsync(actor.ActorId, normalizedLimit, cancellationToken);
        return Result<ScheduledSampleAnnouncementList>.Success(new ScheduledSampleAnnouncementList(scheduled));
    }
}

internal sealed class ProcessDueScheduledSampleAnnouncementsCommandHandler : ICommandHandler<ProcessDueScheduledSampleAnnouncementsCommand, int>
{
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ISampleAnnouncementPublisher _publisher;
    private readonly IScheduledSampleAnnouncementStore _store;

    public ProcessDueScheduledSampleAnnouncementsCommandHandler(
        BuildingBlocks.Domain.Time.IClock clock,
        ISampleAnnouncementPublisher publisher,
        IScheduledSampleAnnouncementStore store)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<int>> Handle(ProcessDueScheduledSampleAnnouncementsCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.GetCurrentInstant();
        var normalizedBatchSize = Math.Clamp(command.BatchSize, 1, SampleAnnouncementSchedulingDefaults.MaxListLimit);
        var leaseId = Guid.NewGuid();
        var leased = await _store.LeaseDueAsync(
            now,
            leaseId,
            now + SampleAnnouncementSchedulingDefaults.ProcessingLeaseDuration,
            normalizedBatchSize,
            cancellationToken);

        var processed = 0;
        foreach (var scheduled in leased)
        {
            var published = await _publisher.PublishAsync(
                new SampleAnnouncementContent(scheduled.Title, scheduled.Body),
                scheduled.ScheduledForUtc,
                scheduled.ScheduledByActorId,
                cancellationToken);

            await _store.MarkPublishedAsync(
                scheduled.ScheduledAnnouncementId,
                scheduled.LeaseId,
                published.AnnouncementId,
                published.PublishedAt,
                cancellationToken);

            processed++;
        }

        return Result<int>.Success(processed);
    }
}

internal sealed record SampleAnnouncementScheduleResolution(Instant ScheduledForUtc, string LocalTimeResolution);

internal static class SampleAnnouncementScheduleResolver
{
    public static bool IsValidTimeZoneId(string timeZoneId)
    {
        return !string.IsNullOrWhiteSpace(timeZoneId)
            && DateTimeZoneProviders.Tzdb.Ids.Contains(timeZoneId);
    }

    public static SampleAnnouncementScheduleResolution ResolveScheduledInstant(LocalDate scheduledLocalDate, LocalTime scheduledLocalTime, string timeZoneId)
    {
        var zone = DateTimeZoneProviders.Tzdb[timeZoneId];
        var localDateTime = scheduledLocalDate + scheduledLocalTime;
        var mapping = zone.MapLocal(localDateTime);

        return mapping.Count switch
        {
            0 => new SampleAnnouncementScheduleResolution(
                zone.AtLeniently(localDateTime).ToInstant(),
                SampleAnnouncementLocalTimeResolutions.SkippedForward),
            1 => new SampleAnnouncementScheduleResolution(
                zone.AtStrictly(localDateTime).ToInstant(),
                SampleAnnouncementLocalTimeResolutions.Exact),
            _ => new SampleAnnouncementScheduleResolution(
                zone.ResolveLocal(
                    localDateTime,
                    Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ThrowWhenSkipped)).ToInstant(),
                SampleAnnouncementLocalTimeResolutions.AmbiguousEarlier)
        };
    }
}
