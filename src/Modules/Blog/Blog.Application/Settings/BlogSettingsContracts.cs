using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Blog.Application.Authorization;
using NodaTime;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Blog.Application.Settings;

public sealed record BlogModuleSettings(
    string OperatorSummary,
    int PreviewLimit,
    int Version,
    Instant UpdatedUtc,
    string UpdatedByActorId);

public interface IBlogSettingsStore
{
    ValueTask<BlogModuleSettings> GetAsync(CancellationToken cancellationToken);

    ValueTask<Result<BlogModuleSettings>> UpdateAsync(
        int expectedVersion,
        string operatorSummary,
        int previewLimit,
        string actorId,
        Instant now,
        CancellationToken cancellationToken);
}

public static class BlogSettingsErrors
{
    public static Error ConcurrencyConflict()
    {
        return new Error(
            "blog.settings_version_conflict",
            "Blog settings were changed by another operation.",
            ErrorKind.Conflict);
    }

    public static Error OperatorSummaryRequired()
    {
        return new Error(
            "blog.settings_summary_required",
            "Blog settings require an operator summary.",
            ErrorKind.Validation);
    }

    public static Error PreviewLimitMustBePositive()
    {
        return new Error(
            "blog.settings_preview_limit_required",
            "Blog settings require a positive preview limit.",
            ErrorKind.Validation);
    }

    public static Error VersionMustBePositive()
    {
        return new Error(
            "blog.settings_version_required",
            "Blog settings updates require a positive expected version.",
            ErrorKind.Validation);
    }
}

public static class BlogSettingsDefaults
{
    public const string SettingsTargetId = "default";

    public static readonly Duration RecentAuthenticationWindow = Duration.FromMinutes(5);
}

public sealed record GetBlogSettingsQuery : IQuery<BlogModuleSettings>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetBlogSettingsQueryHandler : IQueryHandler<GetBlogSettingsQuery, BlogModuleSettings>
{
    private readonly IBlogSettingsStore _store;

    public GetBlogSettingsQueryHandler(IBlogSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogModuleSettings>> Handle(GetBlogSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Result<BlogModuleSettings>.Success(settings);
    }
}

public sealed record UpdateBlogSettingsCommand(int ExpectedVersion, string OperatorSummary, int PreviewLimit)
    : ICommand<BlogModuleSettings>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = BlogSettingsDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class UpdateBlogSettingsCommandHandler : ICommandHandler<UpdateBlogSettingsCommand, BlogModuleSettings>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogSettingsStore _store;

    public UpdateBlogSettingsCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogSettingsStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogModuleSettings>> Handle(UpdateBlogSettingsCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<BlogModuleSettings>.Failure(BlogSettingsErrors.VersionMustBePositive());
        }

        if (string.IsNullOrWhiteSpace(command.OperatorSummary))
        {
            return Result<BlogModuleSettings>.Failure(BlogSettingsErrors.OperatorSummaryRequired());
        }

        if (command.PreviewLimit <= 0)
        {
            return Result<BlogModuleSettings>.Failure(BlogSettingsErrors.PreviewLimitMustBePositive());
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.UpdateAsync(command.ExpectedVersion, command.OperatorSummary.Trim(), command.PreviewLimit, actorId, now, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: BlogModuleInfo.ModuleKey,
                Action: "blog.settings.update",
                TargetType: "module-settings",
                TargetId: BlogSettingsDefaults.SettingsTargetId,
                Outcome: "updated",
                OccurredUtc: now,
                ActorId: actorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}
