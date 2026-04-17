using Blog.Application.Authorization;
using Blog.Application.Posts;
using Blog.Domain.Posts;
using Blog.PublicContracts.Events;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Blog.Application.SharedReads;
using NodaTime;
using NodaTime.TimeZones;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Blog.Application.Scheduling;

public static class BlogPostSchedulingDefaults
{
    public const int DefaultProcessingBatchSize = 10;
    public const int MaxProcessingBatchSize = 25;
}

public static class BlogPostLocalTimeResolutions
{
    public const string Exact = "exact";
    public const string SkippedForward = "skipped_forward";
    public const string AmbiguousEarlier = "ambiguous_earlier";
}

public enum BlogScheduledTransitionKind
{
    Publish = 0,
    Unpublish = 1,
}

public sealed record ProcessedBlogPostTransition(
    BlogScheduledTransitionKind Kind,
    BlogPost Post,
    Instant EffectiveUtc,
    string ActorId);

public static class BlogPostSchedulingErrors
{
    public static Error CurrentActorRequired()
    {
        return new Error(
            "blog.schedule_current_actor_required",
            "A signed-in actor is required to update blog publication schedules.",
            ErrorKind.Unauthorized);
    }

    public static Error PublishMustBeFuture()
    {
        return new Error(
            "blog.schedule_publish_must_be_future",
            "The scheduled publish time must be in the future.",
            ErrorKind.Validation);
    }

    public static Error PublishAlreadyPublished()
    {
        return new Error(
            "blog.schedule_publish_already_published",
            "Scheduled publish cannot be set while the post is already published.",
            ErrorKind.Validation);
    }

    public static Error ScheduleDateAndTimeRequired(string scheduleKind)
    {
        return new Error(
            $"blog.schedule_{scheduleKind}_date_time_required",
            $"The scheduled {scheduleKind} date and time must both be provided.",
            ErrorKind.Validation);
    }

    public static Error TimeZoneRequired()
    {
        return new Error(
            "blog.schedule_time_zone_required",
            "The current actor does not have a preferred time zone configured.",
            ErrorKind.Validation);
    }

    public static Error InvalidTimeZone(string timeZoneId)
    {
        return new Error(
            "blog.schedule_time_zone_invalid",
            $"The current actor preferred time zone '{timeZoneId}' is not a valid IANA time zone id.",
            ErrorKind.Validation);
    }

    public static Error TimeZoneReadUnavailable()
    {
        return new Error(
            "blog.schedule_time_zone_unavailable",
            "The current actor preferred time zone could not be confirmed within the bounded shared-read policy.",
            ErrorKind.ServiceUnavailable);
    }

    public static Error UnpublishMustBeFuture()
    {
        return new Error(
            "blog.schedule_unpublish_must_be_future",
            "The scheduled unpublish time must be in the future.",
            ErrorKind.Validation);
    }

    public static Error UnpublishMustFollowPublish()
    {
        return new Error(
            "blog.schedule_unpublish_before_publish",
            "The scheduled unpublish time must be after the effective publish time.",
            ErrorKind.Validation);
    }

    public static Error UnpublishRequiresPublishedState()
    {
        return new Error(
            "blog.schedule_unpublish_requires_published_state",
            "Scheduled unpublish requires a currently published post or a scheduled publish time.",
            ErrorKind.Validation);
    }
}

public sealed record ScheduleBlogPostLifecycleCommand(
    Guid PostId,
    int ExpectedVersion,
    LocalDate? PublishLocalDate,
    LocalTime? PublishLocalTime,
    LocalDate? UnpublishLocalDate,
    LocalTime? UnpublishLocalTime)
    : ICommand<BlogPost>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ScheduleBlogPostLifecycleCommandValidator : IRequestValidator<ScheduleBlogPostLifecycleCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(ScheduleBlogPostLifecycleCommand request, CancellationToken cancellationToken)
    {
        var error = BlogPostNormalization.ValidatePositiveVersion(request.ExpectedVersion);
        return Task.FromResult<IReadOnlyList<Error>>(error is null ? Array.Empty<Error>() : [error]);
    }
}

public sealed record ProcessDueBlogPostSchedulesCommand(int BatchSize = BlogPostSchedulingDefaults.DefaultProcessingBatchSize)
    : ICommand<int>, IModuleScoped
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;
}

internal sealed class ScheduleBlogPostLifecycleCommandHandler : ICommandHandler<ScheduleBlogPostLifecycleCommand, BlogPost>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogIdentityTimeZoneReader _timeZoneReader;
    private readonly IBlogPostStore _store;

    public ScheduleBlogPostLifecycleCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogPostStore store,
        IBlogIdentityTimeZoneReader timeZoneReader)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeZoneReader = timeZoneReader ?? throw new ArgumentNullException(nameof(timeZoneReader));
    }

    public async Task<Result<BlogPost>> Handle(ScheduleBlogPostLifecycleCommand command, CancellationToken cancellationToken)
    {
        var current = await _store.GetByIdAsync(command.PostId, cancellationToken);
        if (current.IsFailure)
        {
            return current;
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.ActorId))
        {
            return Result<BlogPost>.Failure(BlogPostSchedulingErrors.CurrentActorRequired());
        }

        var timeZoneId = await _timeZoneReader.GetRequiredTimeZoneIdAsync(actor.ActorId, cancellationToken);
        if (timeZoneId.IsFailure)
        {
            return Result<BlogPost>.Failure(timeZoneId.Error);
        }

        var now = _clock.GetCurrentInstant();
        var publish = ResolvePublication(
            command.PublishLocalDate,
            command.PublishLocalTime,
            timeZoneId.Value!,
            actor.ActorId,
            scheduleKind: "publish");
        if (publish.IsFailure)
        {
            return Result<BlogPost>.Failure(publish.Error);
        }

        var unpublish = ResolvePublication(
            command.UnpublishLocalDate,
            command.UnpublishLocalTime,
            timeZoneId.Value!,
            actor.ActorId,
            scheduleKind: "unpublish");
        if (unpublish.IsFailure)
        {
            return Result<BlogPost>.Failure(unpublish.Error);
        }

        if (publish.Value is not null && publish.Value.ScheduledForUtc <= now)
        {
            return Result<BlogPost>.Failure(BlogPostSchedulingErrors.PublishMustBeFuture());
        }

        if (unpublish.Value is not null && unpublish.Value.ScheduledForUtc <= now)
        {
            return Result<BlogPost>.Failure(BlogPostSchedulingErrors.UnpublishMustBeFuture());
        }

        var effectivePublishUtc = publish.Value?.ScheduledForUtc
            ?? current.Value!.PublicationSchedule.Publish?.ScheduledForUtc
            ?? (current.Value.Status == BlogPostStatus.Published ? current.Value.PublishedUtc : null);

        if (unpublish.Value is not null && effectivePublishUtc is null)
        {
            return Result<BlogPost>.Failure(BlogPostSchedulingErrors.UnpublishRequiresPublishedState());
        }

        if (unpublish.Value is not null && effectivePublishUtc is not null && unpublish.Value.ScheduledForUtc <= effectivePublishUtc.Value)
        {
            return Result<BlogPost>.Failure(BlogPostSchedulingErrors.UnpublishMustFollowPublish());
        }

        var result = await _store.SchedulePublicationAsync(
            command.PostId,
            command.ExpectedVersion,
            new BlogPublicationSchedule(publish.Value, unpublish.Value),
            actor.ActorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogPostNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actor.ActorId,
            now,
            "blog.post.schedule.update",
            command.PostId.ToString(),
            BuildScheduleOutcome(result.Value!.PublicationSchedule),
            cancellationToken);

        return result;
    }

    private static Result<BlogScheduledPublication?> ResolvePublication(
        LocalDate? scheduledLocalDate,
        LocalTime? scheduledLocalTime,
        string timeZoneId,
        string actorId,
        string scheduleKind)
    {
        if (scheduledLocalDate is null && scheduledLocalTime is null)
        {
            return Result<BlogScheduledPublication?>.Success(null);
        }

        if (scheduledLocalDate is null || scheduledLocalTime is null)
        {
            return Result<BlogScheduledPublication?>.Failure(BlogPostSchedulingErrors.ScheduleDateAndTimeRequired(scheduleKind));
        }

        return Result<BlogScheduledPublication?>.Success(
            BlogPublicationScheduleResolver.Resolve(scheduledLocalDate.Value, scheduledLocalTime.Value, timeZoneId, actorId));
    }

    private static string BuildScheduleOutcome(BlogPublicationSchedule publicationSchedule)
    {
        var parts = new List<string>(capacity: 2);
        if (publicationSchedule.Publish is not null)
        {
            parts.Add($"publish:{publicationSchedule.Publish.ScheduledForUtc}");
        }

        if (publicationSchedule.Unpublish is not null)
        {
            parts.Add($"unpublish:{publicationSchedule.Unpublish.ScheduledForUtc}");
        }

        return parts.Count == 0 ? "cleared" : string.Join('|', parts);
    }
}

internal sealed class ProcessDueBlogPostSchedulesCommandHandler : ICommandHandler<ProcessDueBlogPostSchedulesCommand, int>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly IIntegrationEventOutboxPublisher _outboxPublisher;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogPostStore _store;

    public ProcessDueBlogPostSchedulesCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        IIntegrationEventOutboxPublisher outboxPublisher,
        IRequestContextAccessor requestContextAccessor,
        IBlogPostStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _outboxPublisher = outboxPublisher ?? throw new ArgumentNullException(nameof(outboxPublisher));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<int>> Handle(ProcessDueBlogPostSchedulesCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.GetCurrentInstant();
        var batchSize = Math.Clamp(command.BatchSize, 1, BlogPostSchedulingDefaults.MaxProcessingBatchSize);
        var transitions = await _store.ProcessDueScheduledTransitionsAsync(now, batchSize, cancellationToken);

        foreach (var transition in transitions)
        {
            if (transition.Kind == BlogScheduledTransitionKind.Publish)
            {
                await _outboxPublisher.PublishAsync(
                    new IntegrationEventOutboxPublishRequest(
                        BlogModuleInfo.ModuleKey,
                        new BlogPostPublishedEventV1(
                            Guid.NewGuid(),
                            transition.EffectiveUtc,
                            transition.Post.PostId,
                            transition.Post.Slug,
                            transition.Post.Title,
                            transition.Post.Summary,
                            transition.Post.Body,
                            transition.ActorId)),
                    cancellationToken);
            }

            await BlogPostNormalization.WriteAuditAsync(
                _auditEventWriter,
                _requestContextAccessor,
                transition.ActorId,
                now,
                transition.Kind == BlogScheduledTransitionKind.Publish ? "blog.post.publish.scheduled" : "blog.post.unpublish.scheduled",
                transition.Post.PostId.ToString(),
                transition.Kind == BlogScheduledTransitionKind.Publish ? BlogPostStatusNames.Published : BlogPostStatusNames.Archived,
                cancellationToken);
        }

        return Result<int>.Success(transitions.Count);
    }
}

internal static class BlogPublicationScheduleResolver
{
    public static bool IsValidTimeZoneId(string timeZoneId)
    {
        return !string.IsNullOrWhiteSpace(timeZoneId)
            && DateTimeZoneProviders.Tzdb.Ids.Contains(timeZoneId);
    }

    public static BlogScheduledPublication Resolve(
        LocalDate scheduledLocalDate,
        LocalTime scheduledLocalTime,
        string timeZoneId,
        string actorId)
    {
        var zone = DateTimeZoneProviders.Tzdb[timeZoneId];
        var localDateTime = scheduledLocalDate + scheduledLocalTime;
        var mapping = zone.MapLocal(localDateTime);

        return mapping.Count switch
        {
            0 => new BlogScheduledPublication(
                scheduledLocalDate,
                scheduledLocalTime,
                timeZoneId,
                BlogPostLocalTimeResolutions.SkippedForward,
                zone.AtLeniently(localDateTime).ToInstant(),
                actorId),
            1 => new BlogScheduledPublication(
                scheduledLocalDate,
                scheduledLocalTime,
                timeZoneId,
                BlogPostLocalTimeResolutions.Exact,
                zone.AtStrictly(localDateTime).ToInstant(),
                actorId),
            _ => new BlogScheduledPublication(
                scheduledLocalDate,
                scheduledLocalTime,
                timeZoneId,
                BlogPostLocalTimeResolutions.AmbiguousEarlier,
                zone.ResolveLocal(
                    localDateTime,
                    Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ThrowWhenSkipped)).ToInstant(),
                actorId)
        };
    }
}
