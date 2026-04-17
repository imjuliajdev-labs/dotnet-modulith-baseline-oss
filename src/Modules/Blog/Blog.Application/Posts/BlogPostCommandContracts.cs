using Blog.Application.Authorization;
using Blog.Application.Scheduling;
using Blog.Domain.Posts;
using Blog.PublicContracts.Events;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Blog.Application.Posts;

public sealed record CreateBlogPostCommand(
    string? Slug,
    string Title,
    string Summary,
    string Body,
    bool Featured,
    string? CategorySlug,
    IReadOnlyCollection<string>? TagNames,
    BlogSeoMetadata SeoMetadata,
    IReadOnlyCollection<string>? ShareTargets)
    : ICommand<BlogPost>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class CreateBlogPostCommandValidator : IRequestValidator<CreateBlogPostCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(CreateBlogPostCommand request, CancellationToken cancellationToken)
    {
        var errors = BlogPostNormalization.ValidateDraft(
            request.Slug,
            request.Title,
            request.Summary,
            request.Body,
            request.CategorySlug,
            request.TagNames,
            request.SeoMetadata,
            request.ShareTargets);
        return Task.FromResult(errors);
    }
}

internal sealed class CreateBlogPostCommandHandler : ICommandHandler<CreateBlogPostCommand, BlogPost>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogPostStore _store;

    public CreateBlogPostCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogPostStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogPost>> Handle(CreateBlogPostCommand command, CancellationToken cancellationToken)
    {
        if (!BlogPostNormalization.TryCreateDraft(
            command.Slug,
            command.Title,
            command.Summary,
            command.Body,
            command.CategorySlug,
            command.TagNames,
            command.SeoMetadata,
            command.ShareTargets,
            out var draft,
            out var error))
        {
            return Result<BlogPost>.Failure(error!);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.CreateAsync(
            draft!,
            command.Featured,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogPostNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.post.create",
            result.Value!.PostId.ToString(),
            result.Value.Status.ToString().ToLowerInvariant(),
            cancellationToken);

        return result;
    }
}

public sealed record UpdateBlogPostCommand(
    Guid PostId,
    int ExpectedVersion,
    string? Slug,
    string Title,
    string Summary,
    string Body,
    bool Featured,
    string? CategorySlug,
    IReadOnlyCollection<string>? TagNames,
    BlogSeoMetadata SeoMetadata,
    IReadOnlyCollection<string>? ShareTargets)
    : ICommand<BlogPost>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class UpdateBlogPostCommandValidator : IRequestValidator<UpdateBlogPostCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(UpdateBlogPostCommand request, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        var versionError = BlogPostNormalization.ValidatePositiveVersion(request.ExpectedVersion);
        if (versionError is not null)
        {
            errors.Add(versionError);
        }

        errors.AddRange(BlogPostNormalization.ValidateDraft(
            request.Slug,
            request.Title,
            request.Summary,
            request.Body,
            request.CategorySlug,
            request.TagNames,
            request.SeoMetadata,
            request.ShareTargets));

        return Task.FromResult<IReadOnlyList<Error>>(errors);
    }
}

internal sealed class UpdateBlogPostCommandHandler : ICommandHandler<UpdateBlogPostCommand, BlogPost>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogPostStore _store;

    public UpdateBlogPostCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogPostStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogPost>> Handle(UpdateBlogPostCommand command, CancellationToken cancellationToken)
    {
        if (!BlogPostNormalization.TryCreateDraft(
            command.Slug,
            command.Title,
            command.Summary,
            command.Body,
            command.CategorySlug,
            command.TagNames,
            command.SeoMetadata,
            command.ShareTargets,
            out var draft,
            out var error))
        {
            return Result<BlogPost>.Failure(error!);
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.UpdateAsync(
            command.PostId,
            command.ExpectedVersion,
            draft!,
            command.Featured,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await BlogPostNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.post.update",
            command.PostId.ToString(),
            "updated",
            cancellationToken);

        return result;
    }
}

public sealed record PublishBlogPostCommand(Guid PostId, int ExpectedVersion, string RequestKey)
    : IIdempotentCommand<BlogPost>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class PublishBlogPostCommandValidator : IRequestValidator<PublishBlogPostCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(PublishBlogPostCommand request, CancellationToken cancellationToken)
    {
        var error = BlogPostNormalization.ValidatePositiveVersion(request.ExpectedVersion);
        return Task.FromResult<IReadOnlyList<Error>>(error is null ? Array.Empty<Error>() : [error]);
    }
}

internal sealed class PublishBlogPostCommandHandler : ICommandHandler<PublishBlogPostCommand, BlogPost>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IIntegrationEventOutboxPublisher _outboxPublisher;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogPostStore _store;

    public PublishBlogPostCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IIntegrationEventOutboxPublisher outboxPublisher,
        IRequestContextAccessor requestContextAccessor,
        IBlogPostStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _outboxPublisher = outboxPublisher ?? throw new ArgumentNullException(nameof(outboxPublisher));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogPost>> Handle(PublishBlogPostCommand command, CancellationToken cancellationToken)
    {
        var current = await _store.GetByIdAsync(command.PostId, cancellationToken);
        if (current.IsFailure)
        {
            return current;
        }

        if (current.Value!.Status == BlogPostStatus.Published)
        {
            return current.Value.Version == command.ExpectedVersion
                ? current
                : Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(command.PostId));
        }

        if (current.Value.Version != command.ExpectedVersion)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(command.PostId));
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();

        var result = await _store.PublishAsync(
            command.PostId,
            command.ExpectedVersion,
            actorId,
            now,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        var post = result.Value!;
        await _outboxPublisher.PublishAsync(
            new IntegrationEventOutboxPublishRequest(
                BlogModuleInfo.ModuleKey,
                new BlogPostPublishedEventV1(
                    Guid.NewGuid(),
                    post.PublishedUtc ?? now,
                    post.PostId,
                    post.Slug,
                    post.Title,
                    post.Summary,
                    post.Body,
                    actorId)),
            cancellationToken);

        await BlogPostNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.post.publish",
            command.PostId.ToString(),
            BlogPostStatusNames.Published,
            cancellationToken);

        return result;
    }
}

public sealed record SetBlogPostStatusCommand(Guid PostId, int ExpectedVersion, string Status)
    : ICommand<BlogPost>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class SetBlogPostStatusCommandValidator : IRequestValidator<SetBlogPostStatusCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(SetBlogPostStatusCommand request, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        var versionError = BlogPostNormalization.ValidatePositiveVersion(request.ExpectedVersion);
        if (versionError is not null)
        {
            errors.Add(versionError);
        }

        var statusError = BlogPostNormalization.ValidateManagedStatus(request.Status);
        if (statusError is not null)
        {
            errors.Add(statusError);
        }

        return Task.FromResult<IReadOnlyList<Error>>(errors);
    }
}

internal sealed class SetBlogPostStatusCommandHandler : ICommandHandler<SetBlogPostStatusCommand, BlogPost>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IBlogPostStore _store;

    public SetBlogPostStatusCommandHandler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IBlogPostStore store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogPost>> Handle(SetBlogPostStatusCommand command, CancellationToken cancellationToken)
    {
        BlogPost.TryParseStatus(command.Status, out var status);

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.SetStatusAsync(command.PostId, command.ExpectedVersion, status, actorId, now, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await BlogPostNormalization.WriteAuditAsync(
            _auditEventWriter,
            _requestContextAccessor,
            actorId,
            now,
            "blog.post.status.update",
            command.PostId.ToString(),
            status.ToString().ToLowerInvariant(),
            cancellationToken);

        return result;
    }
}

public static class BlogPostStatusNames
{
    public const string Archived = "archived";
    public const string Draft = "draft";
    public const string Published = "published";

    public static string From(BlogPostStatus status)
    {
        return status switch
        {
            BlogPostStatus.Archived => Archived,
            BlogPostStatus.Published => Published,
            _ => Draft,
        };
    }
}

internal static class BlogPostNormalization
{
    public static bool TryCreateDraft(
        string? slug,
        string title,
        string summary,
        string body,
        string? categorySlug,
        IReadOnlyCollection<string>? tagNames,
        BlogSeoMetadata seoMetadata,
        IReadOnlyCollection<string>? shareTargets,
        out BlogPostDraft? draft,
        out Error? error)
    {
        if (BlogPostDraft.TryCreate(
            slug,
            title,
            summary,
            body,
            categorySlug,
            tagNames,
            seoMetadata,
            shareTargets,
            out draft,
            out var validationError))
        {
            error = null;
            return true;
        }

        error = MapDraftError(validationError);
        return false;
    }

    public static IReadOnlyList<Error> ValidateDraft(
        string? slug,
        string title,
        string summary,
        string body,
        string? categorySlug,
        IReadOnlyCollection<string>? tagNames,
        BlogSeoMetadata seoMetadata,
        IReadOnlyCollection<string>? shareTargets)
    {
        return TryCreateDraft(slug, title, summary, body, categorySlug, tagNames, seoMetadata, shareTargets, out _, out var error)
            ? Array.Empty<Error>()
            : [error!];
    }

    public static Error? ValidateManagedStatus(string status)
    {
        if (!BlogPost.TryParseStatus(status, out var parsed))
        {
            return BlogPostErrors.InvalidStatus(status);
        }

        return parsed == BlogPostStatus.Published
            ? BlogPostErrors.PublishRequiresDedicatedEndpoint()
            : null;
    }

    public static string CreateSlug(string value)
    {
        return BlogPostDraft.CreateSlug(value);
    }

    public static Error? ValidatePositiveVersion(int expectedVersion)
    {
        return expectedVersion <= 0 ? BlogPostErrors.VersionMustBePositive() : null;
    }

    public static Error? ValidatePublicSlug(string slug)
    {
        return string.IsNullOrWhiteSpace(slug)
            ? BlogPostErrors.PublishedPostNotFound(string.Empty)
            : null;
    }

    private static Error MapDraftError(BlogPostDraftValidationError error)
    {
        return error switch
        {
            BlogPostDraftValidationError.TitleRequired => BlogPostErrors.TitleRequired(),
            BlogPostDraftValidationError.SummaryRequired => BlogPostErrors.SummaryRequired(),
            BlogPostDraftValidationError.BodyRequired => BlogPostErrors.BodyRequired(),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown blog post draft validation error."),
        };
    }

    internal static Task WriteAuditAsync(
        IAuditEventWriter auditEventWriter,
        IRequestContextAccessor requestContextAccessor,
        string actorId,
        NodaTime.Instant occurredUtc,
        string action,
        string targetId,
        string outcome,
        CancellationToken cancellationToken)
    {
        return auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: BlogModuleInfo.ModuleKey,
                Action: action,
                TargetType: "blog-post",
                TargetId: targetId,
                Outcome: outcome,
                OccurredUtc: occurredUtc,
                ActorId: actorId,
                CorrelationId: requestContextAccessor.Current?.CorrelationId),
            cancellationToken);
    }
}
