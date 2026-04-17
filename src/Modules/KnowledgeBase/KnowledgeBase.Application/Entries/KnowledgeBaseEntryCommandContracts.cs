using BuildingBlocks.Application;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using KnowledgeBase.Application.Authorization;
using KnowledgeBase.Domain.Entries;
using KnowledgeBase.PublicContracts.Events;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace KnowledgeBase.Application.Entries;

public sealed record CreateKnowledgeEntryCommand(
    string? Slug,
    string Title,
    string Body,
    string? Category,
    bool Featured,
    int SortOrder)
    : ICommand<KnowledgeEntry>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class CreateKnowledgeEntryCommandHandler : ICommandHandler<CreateKnowledgeEntryCommand, KnowledgeEntry>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IKnowledgeBaseStore _store;

    public CreateKnowledgeEntryCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IKnowledgeBaseStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeEntry>> Handle(CreateKnowledgeEntryCommand command, CancellationToken cancellationToken)
    {
        var normalized = KnowledgeBaseEntryNormalization.Normalize(command.Slug, command.Title, command.Body, command.Category);
        if (normalized.IsFailure)
        {
            return Result<KnowledgeEntry>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.CreateAsync(
            normalized.Value!.Slug,
            normalized.Value.Title,
            normalized.Value.Body,
            normalized.Value.Category,
            command.Featured,
            command.SortOrder,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await KnowledgeBaseEntryNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "knowledge-base.entry.create",
            result.Value!.EntryId.ToString(),
            result.Value.Status.ToString().ToLowerInvariant(),
            cancellationToken);

        return result;
    }
}

public sealed record UpdateKnowledgeEntryCommand(
    Guid EntryId,
    int ExpectedVersion,
    string? Slug,
    string Title,
    string Body,
    string? Category,
    bool Featured,
    int SortOrder)
    : ICommand<KnowledgeEntry>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class UpdateKnowledgeEntryCommandHandler : ICommandHandler<UpdateKnowledgeEntryCommand, KnowledgeEntry>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IKnowledgeBaseStore _store;

    public UpdateKnowledgeEntryCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IKnowledgeBaseStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeEntry>> Handle(UpdateKnowledgeEntryCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.VersionMustBePositive());
        }

        var normalized = KnowledgeBaseEntryNormalization.Normalize(command.Slug, command.Title, command.Body, command.Category);
        if (normalized.IsFailure)
        {
            return Result<KnowledgeEntry>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.UpdateAsync(
            command.EntryId,
            command.ExpectedVersion,
            normalized.Value!.Slug,
            normalized.Value.Title,
            normalized.Value.Body,
            normalized.Value.Category,
            command.Featured,
            command.SortOrder,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await KnowledgeBaseEntryNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "knowledge-base.entry.update",
            command.EntryId.ToString(),
            "updated",
            cancellationToken);

        return result;
    }
}

public sealed record PublishKnowledgeEntryCommand(Guid EntryId, int ExpectedVersion, string RequestKey)
    : IIdempotentCommand<KnowledgeEntry>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class PublishKnowledgeEntryCommandHandler : ICommandHandler<PublishKnowledgeEntryCommand, KnowledgeEntry>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IIntegrationEventOutboxPublisher _outboxPublisher;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IKnowledgeBaseStore _store;

    public PublishKnowledgeEntryCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IIntegrationEventOutboxPublisher outboxPublisher,
        IRequestContextAccessor requestContextAccessor,
        IKnowledgeBaseStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _outboxPublisher = outboxPublisher ?? throw new ArgumentNullException(nameof(outboxPublisher));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeEntry>> Handle(PublishKnowledgeEntryCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.VersionMustBePositive());
        }

        var current = await _store.GetByIdAsync(command.EntryId, cancellationToken);
        if (current.IsFailure)
        {
            return current;
        }

        if (current.Value!.Status == KnowledgeEntryStatus.Published)
        {
            return current.Value.Version == command.ExpectedVersion
                ? current
                : Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.ConcurrencyConflict(command.EntryId));
        }

        if (current.Value.Version != command.ExpectedVersion)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.ConcurrencyConflict(command.EntryId));
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.SetStatusAsync(
            command.EntryId,
            command.ExpectedVersion,
            KnowledgeEntryStatus.Published,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        var entry = result.Value!;
        var publishedAt = entry.PublishedUtc ?? now;

        await _outboxPublisher.PublishAsync(
            new IntegrationEventOutboxPublishRequest(
                KnowledgeBaseModuleInfo.ModuleKey,
                new KnowledgeEntryPublishedEventV1(
                    Guid.NewGuid(),
                    publishedAt,
                    entry.EntryId,
                    entry.Slug,
                    entry.Title,
                    entry.Body,
                    actorId)),
            cancellationToken);

        await _outboxPublisher.PublishAsync(
            new IntegrationEventOutboxPublishRequest(
                KnowledgeBaseModuleInfo.ModuleKey,
                new KnowledgeEntryPublishedEventV2(
                    Guid.NewGuid(),
                    publishedAt,
                    entry.EntryId,
                    entry.Slug,
                    entry.Title,
                    entry.Body,
                    entry.Category,
                    actorId)),
            cancellationToken);

        await KnowledgeBaseEntryNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "knowledge-base.entry.publish",
            command.EntryId.ToString(),
            KnowledgeEntryStatusNames.Published,
            cancellationToken);

        return result;
    }
}

public sealed record SetKnowledgeEntryStatusCommand(Guid EntryId, int ExpectedVersion, string Status)
    : ICommand<KnowledgeEntry>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class SetKnowledgeEntryStatusCommandHandler : ICommandHandler<SetKnowledgeEntryStatusCommand, KnowledgeEntry>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IKnowledgeBaseStore _store;

    public SetKnowledgeEntryStatusCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IKnowledgeBaseStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeEntry>> Handle(SetKnowledgeEntryStatusCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.VersionMustBePositive());
        }

        if (!KnowledgeBaseEntryNormalization.TryParseStatus(command.Status, out var status))
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.InvalidStatus(command.Status));
        }

        if (status == KnowledgeEntryStatus.Published)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.PublishRequiresDedicatedEndpoint());
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.SetStatusAsync(command.EntryId, command.ExpectedVersion, status, actorId, now, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await KnowledgeBaseEntryNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "knowledge-base.entry.status.update",
            command.EntryId.ToString(),
            status.ToString().ToLowerInvariant(),
            cancellationToken);

        return result;
    }
}
