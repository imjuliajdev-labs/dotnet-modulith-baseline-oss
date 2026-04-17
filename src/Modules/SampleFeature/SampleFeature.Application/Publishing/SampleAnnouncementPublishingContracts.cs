using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Time;
using NodaTime.TimeZones;
using NodaTime;
using SampleFeature.Application.Authorization;
using SampleFeature.Domain.Announcements;
using SampleFeature.PublicContracts.Events;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace SampleFeature.Application.Publishing;

public sealed record PublishSampleAnnouncementCommand(string RequestKey, string Title, string Body) : IIdempotentCommand<PublishedSampleAnnouncement>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => SampleFeatureModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

public sealed record PublishedSampleAnnouncement(
    Guid AnnouncementId,
    string Title,
    string Body,
    Instant PublishedAt,
    string PublishedByActorId);

public interface ISampleAnnouncementWriter
{
    ValueTask WriteAsync(PublishedSampleAnnouncement announcement, CancellationToken cancellationToken);
}

public interface ISampleAnnouncementPublisher
{
    ValueTask<PublishedSampleAnnouncement> PublishAsync(
    SampleAnnouncementContent content,
        Instant publishedAt,
        string publishedByActorId,
        CancellationToken cancellationToken);
}

public sealed class SampleAnnouncementPublisher : ISampleAnnouncementPublisher
{
    private readonly IIntegrationEventOutboxPublisher _outboxPublisher;
    private readonly ISampleAnnouncementWriter _writer;

    public SampleAnnouncementPublisher(IIntegrationEventOutboxPublisher outboxPublisher, ISampleAnnouncementWriter writer)
    {
        _outboxPublisher = outboxPublisher ?? throw new ArgumentNullException(nameof(outboxPublisher));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async ValueTask<PublishedSampleAnnouncement> PublishAsync(
        SampleAnnouncementContent content,
        Instant publishedAt,
        string publishedByActorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var announcement = new PublishedSampleAnnouncement(
            Guid.NewGuid(),
            content.Title,
            content.Body,
            publishedAt,
            string.IsNullOrWhiteSpace(publishedByActorId) ? "unknown" : publishedByActorId.Trim());

        await _writer.WriteAsync(announcement, cancellationToken);
        await _outboxPublisher.PublishAsync(
            new IntegrationEventOutboxPublishRequest(
                SampleFeatureModuleInfo.ModuleKey,
                new SampleAnnouncementPublishedEventV1(
                    Guid.NewGuid(),
                    publishedAt,
                    announcement.AnnouncementId,
                    announcement.Title,
                    announcement.Body,
                    announcement.PublishedByActorId)),
            cancellationToken);

        return announcement;
    }
}

internal sealed class PublishSampleAnnouncementCommandHandler : ICommandHandler<PublishSampleAnnouncementCommand, PublishedSampleAnnouncement>
{
    private static readonly Error TitleRequired = new(
        "sample-feature.announcement_title_required",
        "Announcement title is required.",
        ErrorKind.Validation);

    private static readonly Error BodyRequired = new(
        "sample-feature.announcement_body_required",
        "Announcement body is required.",
        ErrorKind.Validation);

    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly DomainClock _clock;
    private readonly ISampleAnnouncementPublisher _publisher;
    private readonly IRequestContextAccessor _requestContextAccessor;

    public PublishSampleAnnouncementCommandHandler(
        IAuditEventWriter auditEventWriter,
        ICurrentActorAccessor currentActorAccessor,
        DomainClock clock,
        ISampleAnnouncementPublisher publisher,
        IRequestContextAccessor requestContextAccessor)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task<Result<PublishedSampleAnnouncement>> Handle(PublishSampleAnnouncementCommand command, CancellationToken cancellationToken)
    {
        if (!SampleAnnouncementContent.TryCreate(command.Title, command.Body, out var content, out var validationError))
        {
            return Result<PublishedSampleAnnouncement>.Failure(MapValidationError(validationError));
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var publishedAt = _clock.GetCurrentInstant();
        var announcement = await _publisher.PublishAsync(
            content!,
            publishedAt,
            actorId,
            cancellationToken);

        await SampleAnnouncementAuditing.WriteAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            publishedAt,
            SampleAnnouncementAuditing.ActionPublish,
            SampleAnnouncementAuditing.TargetTypeAnnouncement,
            announcement.AnnouncementId.ToString(),
            SampleAnnouncementAuditing.OutcomePublished,
            cancellationToken);

        return Result<PublishedSampleAnnouncement>.Success(announcement);
    }

    private static Error MapValidationError(SampleAnnouncementContentValidationError validationError)
    {
        return validationError switch
        {
            SampleAnnouncementContentValidationError.TitleRequired => TitleRequired,
            SampleAnnouncementContentValidationError.BodyRequired => BodyRequired,
            _ => throw new InvalidOperationException($"Unsupported validation error '{validationError}'.")
        };
    }
}
