using Blog.Application.Authorization;
using Blog.Application.Posts;
using Blog.Domain.Taxonomy;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Blog.Application.Taxonomy;

public sealed record CreateBlogCategoryCommand(
    string? Slug,
    string Name,
    string? Description)
    : ICommand<BlogCategory>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class CreateBlogCategoryCommandHandler : ICommandHandler<CreateBlogCategoryCommand, BlogCategory>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogTaxonomyStore _store;

    public CreateBlogCategoryCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogTaxonomyStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogCategory>> Handle(CreateBlogCategoryCommand command, CancellationToken cancellationToken)
    {
        var normalized = BlogTaxonomyNormalization.NormalizeCategory(command.Slug, command.Name, command.Description);
        if (normalized.IsFailure)
        {
            return Result<BlogCategory>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.CreateCategoryAsync(
            normalized.Value!.Slug,
            normalized.Value.Name,
            normalized.Value.Description,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogTaxonomyNormalization.WriteCategoryAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.category.create",
            result.Value!.Slug,
            result.Value.Slug,
            cancellationToken);

        return result;
    }
}

public sealed record UpdateBlogCategoryCommand(
    string Slug,
    int ExpectedVersion,
    string Name,
    string? Description)
    : ICommand<BlogCategory>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class UpdateBlogCategoryCommandHandler : ICommandHandler<UpdateBlogCategoryCommand, BlogCategory>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogTaxonomyStore _store;

    public UpdateBlogCategoryCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogTaxonomyStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogCategory>> Handle(UpdateBlogCategoryCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<BlogCategory>.Failure(BlogPostErrors.VersionMustBePositive());
        }

        var normalized = BlogTaxonomyNormalization.NormalizeCategory(command.Slug, command.Name, command.Description, slugIsExplicit: true);
        if (normalized.IsFailure)
        {
            return Result<BlogCategory>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.UpdateCategoryAsync(
            normalized.Value!.Slug,
            command.ExpectedVersion,
            normalized.Value.Name,
            normalized.Value.Description,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogTaxonomyNormalization.WriteCategoryAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.category.update",
            result.Value!.Slug,
            result.Value.Slug,
            cancellationToken);

        return result;
    }
}

public sealed record CreateBlogTagCommand(
    string? Slug,
    string DisplayName,
    string? Description)
    : ICommand<BlogTag>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class CreateBlogTagCommandHandler : ICommandHandler<CreateBlogTagCommand, BlogTag>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogTaxonomyStore _store;

    public CreateBlogTagCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogTaxonomyStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogTag>> Handle(CreateBlogTagCommand command, CancellationToken cancellationToken)
    {
        var normalized = BlogTaxonomyNormalization.NormalizeTag(command.Slug, command.DisplayName, command.Description);
        if (normalized.IsFailure)
        {
            return Result<BlogTag>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.CreateTagAsync(
            normalized.Value!.Slug,
            normalized.Value.DisplayName,
            normalized.Value.Description,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogTaxonomyNormalization.WriteTagAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.tag.create",
            result.Value!.Slug,
            result.Value.Slug,
            cancellationToken);

        return result;
    }
}

public sealed record UpdateBlogTagCommand(
    string Slug,
    int ExpectedVersion,
    string DisplayName,
    string? Description)
    : ICommand<BlogTag>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class UpdateBlogTagCommandHandler : ICommandHandler<UpdateBlogTagCommand, BlogTag>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogTaxonomyStore _store;

    public UpdateBlogTagCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogTaxonomyStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogTag>> Handle(UpdateBlogTagCommand command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<BlogTag>.Failure(BlogPostErrors.VersionMustBePositive());
        }

        var normalized = BlogTaxonomyNormalization.NormalizeTag(command.Slug, command.DisplayName, command.Description, slugIsExplicit: true);
        if (normalized.IsFailure)
        {
            return Result<BlogTag>.Failure(normalized.Error);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.UpdateTagAsync(
            normalized.Value!.Slug,
            command.ExpectedVersion,
            normalized.Value.DisplayName,
            normalized.Value.Description,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogTaxonomyNormalization.WriteTagAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.tag.update",
            result.Value!.Slug,
            result.Value.Slug,
            cancellationToken);

        return result;
    }
}
