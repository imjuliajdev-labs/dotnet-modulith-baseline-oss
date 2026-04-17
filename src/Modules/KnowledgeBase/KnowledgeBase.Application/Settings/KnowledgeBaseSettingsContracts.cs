using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using KnowledgeBase.Application.Authorization;
using NodaTime;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace KnowledgeBase.Application.Settings;

public sealed record KnowledgeBaseModuleSettings(
    string PublicExperienceTitle,
    string PublicExperienceBlurb,
    string SearchPlaceholder,
    bool SearchEnabled,
    int ManagementPreviewLimit,
    int Version,
    Instant UpdatedUtc,
    string UpdatedByActorId);

public sealed record NormalizedKnowledgeBaseSettings(
    string PublicExperienceTitle,
    string PublicExperienceBlurb,
    string SearchPlaceholder,
    bool SearchEnabled,
    int ManagementPreviewLimit);

public interface IKnowledgeBaseSettingsStore
{
    ValueTask<KnowledgeBaseModuleSettings> GetAsync(CancellationToken cancellationToken);

    ValueTask<Result<KnowledgeBaseModuleSettings>> UpdateAsync(
        int expectedVersion,
        NormalizedKnowledgeBaseSettings settings,
        string actorId,
        Instant now,
        CancellationToken cancellationToken);
}

public static class KnowledgeBaseModuleSettingsDefaults
{
    public const int DefaultManagementPreviewLimit = 12;
    public const int MaxManagementPreviewLimit = 24;
    public const int MinManagementPreviewLimit = 1;
    public const string DefaultPublicExperienceBlurb = "A generic knowledge surface for FAQ-style publishing today and broader help content later.";
    public const string DefaultPublicExperienceTitle = "Knowledge Base";
    public const string DefaultSearchPlaceholder = "Search by title, answer, or category";
    public const string SettingsTargetId = "default";

    public static readonly Duration RecentAuthenticationWindow = Duration.FromMinutes(5);
}

public static class KnowledgeBaseSettingsErrors
{
    public static Error ConcurrencyConflict()
    {
        return new Error(
            "knowledge-base.settings_version_conflict",
            "Knowledge base settings were changed by another operation.",
            ErrorKind.Conflict);
    }

    public static Error PublicExperienceBlurbRequired()
    {
        return new Error(
            "knowledge-base.settings_blurb_required",
            "Knowledge base settings require a public experience blurb.",
            ErrorKind.Validation);
    }

    public static Error PublicExperienceTitleRequired()
    {
        return new Error(
            "knowledge-base.settings_title_required",
            "Knowledge base settings require a public experience title.",
            ErrorKind.Validation);
    }

    public static Error SearchPlaceholderRequired()
    {
        return new Error(
            "knowledge-base.settings_search_placeholder_required",
            "Knowledge base settings require a search placeholder when search is enabled.",
            ErrorKind.Validation);
    }

    public static Error PreviewLimitOutOfRange(int limit)
    {
        return new Error(
            "knowledge-base.settings_preview_limit_invalid",
            $"Knowledge base settings preview limit '{limit}' must be between {KnowledgeBaseModuleSettingsDefaults.MinManagementPreviewLimit} and {KnowledgeBaseModuleSettingsDefaults.MaxManagementPreviewLimit}.",
            ErrorKind.Validation);
    }

    public static Error VersionMustBePositive()
    {
        return new Error(
            "knowledge-base.settings_version_required",
            "A positive expected version is required when updating knowledge base settings.",
            ErrorKind.Validation);
    }
}

public sealed record GetKnowledgeBasePublicSettingsQuery : IQuery<KnowledgeBaseModuleSettings>, IModuleScoped
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;
}

internal sealed class GetKnowledgeBasePublicSettingsQueryHandler : IQueryHandler<GetKnowledgeBasePublicSettingsQuery, KnowledgeBaseModuleSettings>
{
    private readonly IKnowledgeBaseSettingsStore _store;

    public GetKnowledgeBasePublicSettingsQueryHandler(IKnowledgeBaseSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeBaseModuleSettings>> Handle(GetKnowledgeBasePublicSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Result<KnowledgeBaseModuleSettings>.Success(settings);
    }
}

public sealed record GetKnowledgeBaseManagementSettingsQuery : IQuery<KnowledgeBaseModuleSettings>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetKnowledgeBaseManagementSettingsQueryHandler : IQueryHandler<GetKnowledgeBaseManagementSettingsQuery, KnowledgeBaseModuleSettings>
{
    private readonly IKnowledgeBaseSettingsStore _store;

    public GetKnowledgeBaseManagementSettingsQueryHandler(IKnowledgeBaseSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeBaseModuleSettings>> Handle(GetKnowledgeBaseManagementSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Result<KnowledgeBaseModuleSettings>.Success(settings);
    }
}

public sealed record UpdateKnowledgeBaseSettingsCommand(
    int ExpectedVersion,
    string PublicExperienceTitle,
    string PublicExperienceBlurb,
    string SearchPlaceholder,
    bool SearchEnabled,
    int ManagementPreviewLimit)
    : ICommand<KnowledgeBaseModuleSettings>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];

    public Duration RecentAuthenticationWindow { get; } = KnowledgeBaseModuleSettingsDefaults.RecentAuthenticationWindow;
}

internal sealed class UpdateKnowledgeBaseSettingsCommandHandler : ICommandHandler<UpdateKnowledgeBaseSettingsCommand, KnowledgeBaseModuleSettings>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IKnowledgeBaseSettingsStore _store;

    public UpdateKnowledgeBaseSettingsCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IKnowledgeBaseSettingsStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeBaseModuleSettings>> Handle(UpdateKnowledgeBaseSettingsCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<KnowledgeBaseModuleSettings>.Failure(KnowledgeBaseSettingsErrors.VersionMustBePositive());
        }

        var normalized = KnowledgeBaseSettingsValidation.Normalize(
            command.PublicExperienceTitle,
            command.PublicExperienceBlurb,
            command.SearchPlaceholder,
            command.SearchEnabled,
            command.ManagementPreviewLimit);
        if (normalized.IsFailure)
        {
            return Result<KnowledgeBaseModuleSettings>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.UpdateAsync(command.ExpectedVersion, normalized.Value!, actorId, now, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: KnowledgeBaseModuleInfo.ModuleKey,
                Action: "knowledge-base.settings.update",
                TargetType: "knowledge-base-settings",
                TargetId: KnowledgeBaseModuleSettingsDefaults.SettingsTargetId,
                Outcome: "updated",
                OccurredUtc: now,
                ActorId: actorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

internal static class KnowledgeBaseSettingsValidation
{
    public static Result<NormalizedKnowledgeBaseSettings> Normalize(
        string publicExperienceTitle,
        string publicExperienceBlurb,
        string searchPlaceholder,
        bool searchEnabled,
        int managementPreviewLimit)
    {
        var normalizedTitle = publicExperienceTitle.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return Result<NormalizedKnowledgeBaseSettings>.Failure(KnowledgeBaseSettingsErrors.PublicExperienceTitleRequired());
        }

        var normalizedBlurb = publicExperienceBlurb.Trim();
        if (string.IsNullOrWhiteSpace(normalizedBlurb))
        {
            return Result<NormalizedKnowledgeBaseSettings>.Failure(KnowledgeBaseSettingsErrors.PublicExperienceBlurbRequired());
        }

        if (managementPreviewLimit < KnowledgeBaseModuleSettingsDefaults.MinManagementPreviewLimit
            || managementPreviewLimit > KnowledgeBaseModuleSettingsDefaults.MaxManagementPreviewLimit)
        {
            return Result<NormalizedKnowledgeBaseSettings>.Failure(KnowledgeBaseSettingsErrors.PreviewLimitOutOfRange(managementPreviewLimit));
        }

        var normalizedSearchPlaceholder = searchPlaceholder.Trim();
        if (searchEnabled && string.IsNullOrWhiteSpace(normalizedSearchPlaceholder))
        {
            return Result<NormalizedKnowledgeBaseSettings>.Failure(KnowledgeBaseSettingsErrors.SearchPlaceholderRequired());
        }

        if (string.IsNullOrWhiteSpace(normalizedSearchPlaceholder))
        {
            normalizedSearchPlaceholder = KnowledgeBaseModuleSettingsDefaults.DefaultSearchPlaceholder;
        }

        return Result<NormalizedKnowledgeBaseSettings>.Success(
            new NormalizedKnowledgeBaseSettings(
                normalizedTitle,
                normalizedBlurb,
                normalizedSearchPlaceholder,
                searchEnabled,
                managementPreviewLimit));
    }
}
