using System.Text;
using NodaTime;

namespace Blog.Domain.Posts;

public enum BlogPostDraftValidationError
{
    None = 0,
    TitleRequired = 1,
    SummaryRequired = 2,
    BodyRequired = 3,
}

public enum BlogPublicationScheduleUpdateError
{
    None = 0,
    PublishAlreadyPublished = 1,
    UnpublishRequiresPublishedState = 2,
}

public enum BlogPostStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public sealed record BlogSeoMetadata(
    string? Title,
    string? Description,
    string? Keywords);

public sealed record BlogScheduledPublication(
    LocalDate ScheduledLocalDate,
    LocalTime ScheduledLocalTime,
    string TimeZoneId,
    string LocalTimeResolution,
    Instant ScheduledForUtc,
    string ScheduledByActorId);

public sealed record BlogPublicationSchedule(
    BlogScheduledPublication? Publish,
    BlogScheduledPublication? Unpublish)
{
    public static BlogPublicationSchedule Empty { get; } = new(null, null);

    public bool HasAny => Publish is not null || Unpublish is not null;

    public BlogPublicationSchedule WithoutPublish()
    {
        return Publish is null ? this : new BlogPublicationSchedule(null, Unpublish);
    }

    public BlogPublicationSchedule WithoutUnpublish()
    {
        return Unpublish is null ? this : new BlogPublicationSchedule(Publish, null);
    }
}

public sealed record BlogPostDraft(
    string Slug,
    string Title,
    string Summary,
    string Body,
    string? CategorySlug,
    IReadOnlyCollection<string> TagNames,
    BlogSeoMetadata SeoMetadata,
    IReadOnlyCollection<string> ShareTargets)
{
    public static bool TryCreate(
        string? slug,
        string title,
        string summary,
        string body,
        string? categorySlug,
        IReadOnlyCollection<string>? tagNames,
        BlogSeoMetadata seoMetadata,
        IReadOnlyCollection<string>? shareTargets,
        out BlogPostDraft? draft,
        out BlogPostDraftValidationError error)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            draft = null;
            error = BlogPostDraftValidationError.TitleRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            draft = null;
            error = BlogPostDraftValidationError.SummaryRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            draft = null;
            error = BlogPostDraftValidationError.BodyRequired;
            return false;
        }

        ArgumentNullException.ThrowIfNull(seoMetadata);

        var normalizedTitle = title.Trim();
        var normalizedSummary = summary.Trim();
        var normalizedBody = body.Trim();

        draft = new BlogPostDraft(
            CreateSlug(string.IsNullOrWhiteSpace(slug) ? normalizedTitle : slug.Trim()),
            normalizedTitle,
            normalizedSummary,
            normalizedBody,
            NormalizeOptionalSlug(categorySlug),
            NormalizeSlugValues(tagNames),
            NormalizeSeoMetadata(seoMetadata),
            NormalizeSlugValues(shareTargets));
        error = BlogPostDraftValidationError.None;
        return true;
    }

    public static string CreateSlug(string value)
    {
        var builder = new StringBuilder();
        var previousWasSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator || builder.Length == 0)
            {
                continue;
            }

            builder.Append('-');
            previousWasSeparator = true;
        }

        while (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length -= 1;
        }

        return builder.Length == 0 ? "blog-post" : builder.ToString();
    }

    private static string? NormalizeOptionalSlug(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : CreateSlug(value.Trim());
    }

    private static IReadOnlyCollection<string> NormalizeSlugValues(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(CreateSlug)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static BlogSeoMetadata NormalizeSeoMetadata(BlogSeoMetadata metadata)
    {
        return new BlogSeoMetadata(
            NormalizeOptionalText(metadata.Title),
            NormalizeOptionalText(metadata.Description),
            NormalizeOptionalText(metadata.Keywords));
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record BlogPost(
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    string Body,
    bool Featured,
    BlogPostStatus Status,
    int Version,
    int RevisionNumber,
    string? CategorySlug,
    IReadOnlyCollection<string> TagNames,
    BlogSeoMetadata SeoMetadata,
    IReadOnlyCollection<string> ShareTargets,
    long ViewCount,
    Instant CreatedUtc,
    Instant UpdatedUtc,
    string UpdatedByActorId,
    Instant? PublishedUtc,
    string? PublishedByActorId,
    BlogPublicationSchedule PublicationSchedule)
{
    public static BlogPost CreateDraft(Guid postId, BlogPostDraft draft, bool featured, string actorId, Instant now)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new BlogPost(
            postId,
            draft.Slug,
            draft.Title,
            draft.Summary,
            draft.Body,
            featured,
            BlogPostStatus.Draft,
            1,
            1,
            draft.CategorySlug,
            draft.TagNames,
            draft.SeoMetadata,
            draft.ShareTargets,
            0,
            now,
            now,
            actorId,
            null,
            null,
            BlogPublicationSchedule.Empty);
    }

    public BlogPost ApplyDraft(BlogPostDraft draft, bool featured, string actorId, Instant updatedUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return this with
        {
            Slug = draft.Slug,
            Title = draft.Title,
            Summary = draft.Summary,
            Body = draft.Body,
            Featured = featured,
            Version = Version + 1,
            RevisionNumber = RevisionNumber + 1,
            CategorySlug = draft.CategorySlug,
            TagNames = draft.TagNames,
            SeoMetadata = draft.SeoMetadata,
            ShareTargets = draft.ShareTargets,
            UpdatedUtc = updatedUtc,
            UpdatedByActorId = actorId,
        };
    }

    public BlogPost ApplyStatus(BlogPostStatus status, string actorId, Instant updatedUtc)
    {
        return status switch
        {
            BlogPostStatus.Published => Publish(actorId, updatedUtc, updatedUtc),
            BlogPostStatus.Draft => this with
            {
                Status = BlogPostStatus.Draft,
                Version = Version + 1,
                UpdatedUtc = updatedUtc,
                UpdatedByActorId = actorId,
                PublishedUtc = null,
                PublishedByActorId = null,
                PublicationSchedule = BlogPublicationSchedule.Empty,
            },
            _ => this with
            {
                Status = BlogPostStatus.Archived,
                Version = Version + 1,
                UpdatedUtc = updatedUtc,
                UpdatedByActorId = actorId,
                PublicationSchedule = BlogPublicationSchedule.Empty,
            },
        };
    }

    public BlogPost Publish(string actorId, Instant publishedUtc, Instant updatedUtc)
    {
        return this with
        {
            Status = BlogPostStatus.Published,
            Version = Version + 1,
            UpdatedUtc = updatedUtc,
            UpdatedByActorId = actorId,
            PublishedUtc = publishedUtc,
            PublishedByActorId = actorId,
            PublicationSchedule = PublicationSchedule.WithoutPublish(),
        };
    }

    public BlogPost ClearScheduledPublish(string actorId, Instant updatedUtc)
    {
        if (PublicationSchedule.Publish is null)
        {
            return this;
        }

        return this with
        {
            Version = Version + 1,
            UpdatedUtc = updatedUtc,
            UpdatedByActorId = actorId,
            PublicationSchedule = PublicationSchedule.WithoutPublish(),
        };
    }

    public BlogPost ClearScheduledUnpublish(string actorId, Instant updatedUtc)
    {
        if (PublicationSchedule.Unpublish is null)
        {
            return this;
        }

        return this with
        {
            Version = Version + 1,
            UpdatedUtc = updatedUtc,
            UpdatedByActorId = actorId,
            PublicationSchedule = PublicationSchedule.WithoutUnpublish(),
        };
    }

    public BlogPost ApplyScheduledPublish(string actorId, Instant effectivePublishedUtc, Instant updatedUtc)
    {
        return Publish(actorId, effectivePublishedUtc, updatedUtc);
    }

    public BlogPost ApplyScheduledUnpublish(string actorId, Instant updatedUtc)
    {
        return this with
        {
            Status = BlogPostStatus.Archived,
            Version = Version + 1,
            UpdatedUtc = updatedUtc,
            UpdatedByActorId = actorId,
            PublicationSchedule = PublicationSchedule.WithoutUnpublish(),
        };
    }

    public BlogPost RegisterView()
    {
        return this with { ViewCount = ViewCount + 1 };
    }

    public bool TryApplyPublicationSchedule(
        BlogPublicationSchedule publicationSchedule,
        string actorId,
        Instant updatedUtc,
        out BlogPost updatedPost,
        out BlogPublicationScheduleUpdateError error)
    {
        ArgumentNullException.ThrowIfNull(publicationSchedule);

        if (Status == BlogPostStatus.Published && publicationSchedule.Publish is not null)
        {
            updatedPost = this;
            error = BlogPublicationScheduleUpdateError.PublishAlreadyPublished;
            return false;
        }

        if (publicationSchedule.Unpublish is not null
            && publicationSchedule.Publish is null
            && Status != BlogPostStatus.Published)
        {
            updatedPost = this;
            error = BlogPublicationScheduleUpdateError.UnpublishRequiresPublishedState;
            return false;
        }

        updatedPost = PublicationSchedule == publicationSchedule
            ? this
            : this with
            {
                PublicationSchedule = publicationSchedule,
                Version = Version + 1,
                UpdatedUtc = updatedUtc,
                UpdatedByActorId = actorId,
            };
        error = BlogPublicationScheduleUpdateError.None;
        return true;
    }

    public static bool TryParseStatus(string status, out BlogPostStatus parsed)
    {
        switch (status.Trim().ToLowerInvariant())
        {
            case "published":
                parsed = BlogPostStatus.Published;
                return true;
            case "archived":
                parsed = BlogPostStatus.Archived;
                return true;
            case "draft":
                parsed = BlogPostStatus.Draft;
                return true;
            default:
                parsed = BlogPostStatus.Draft;
                return false;
        }
    }
}
