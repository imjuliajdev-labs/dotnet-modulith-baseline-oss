using System.Text.Json;
using Blog.Application.Posts;
using Blog.Application.Scheduling;
using Blog.Application.Taxonomy;
using Blog.Domain.Posts;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace Blog.Infrastructure.Persistence;

internal sealed class BlogPostStore : IBlogPostStore
{
    private static readonly JsonSerializerOptions JsonOptions = StarterJsonSerializerOptions.Create();
    private readonly IDbContextFactory<BlogPersistenceDbContext> _dbContextFactory;

    public BlogPostStore(IDbContextFactory<BlogPersistenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async ValueTask<Result<BlogPost>> GetByIdAsync(Guid postId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await dbContext.Posts
            .AsNoTracking()
            .Include(post => post.Tags)
            .Include(post => post.ShareTargets)
            .Include(post => post.PublicationSchedule)
            .AsSplitQuery()
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);

        return record is null
            ? Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(postId))
            : Result<BlogPost>.Success(Map(record));
    }

    public async ValueTask<Result<BlogPost>> CreateAsync(
        BlogPostDraft draft,
        bool featured,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var taxonomyError = await ValidateTaxonomyAsync(dbContext, draft.CategorySlug, draft.TagNames, cancellationToken);
        if (taxonomyError is not null)
        {
            return Result<BlogPost>.Failure(taxonomyError);
        }

        var post = BlogPost.CreateDraft(Guid.NewGuid(), draft, featured, actorId, now);
        var record = CreateRecord(post);

        dbContext.Posts.Add(record);
        dbContext.PostRevisions.Add(CreateRevision(post, actorId, now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogPost>.Success(Map(record));
        }
        catch (DbUpdateException exception) when (IsSlugConflict(exception))
        {
            return Result<BlogPost>.Failure(BlogPostErrors.SlugAlreadyExists(draft.Slug));
        }
    }

    public async ValueTask<Result<BlogPost>> UpdateAsync(
        Guid postId,
        int expectedVersion,
        BlogPostDraft draft,
        bool featured,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await LoadMutablePostAsync(dbContext, postId, cancellationToken);
        if (record is null)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(postId));
        }

        if (record.Version != expectedVersion)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }

        var taxonomyError = await ValidateTaxonomyAsync(dbContext, draft.CategorySlug, draft.TagNames, cancellationToken);
        if (taxonomyError is not null)
        {
            return Result<BlogPost>.Failure(taxonomyError);
        }

        var updatedPost = Map(record).ApplyDraft(draft, featured, actorId, now);
        ApplyPost(dbContext, record, updatedPost);
        dbContext.PostRevisions.Add(CreateRevision(updatedPost, actorId, now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogPost>.Success(Map(record));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }
        catch (DbUpdateException exception) when (IsSlugConflict(exception))
        {
            return Result<BlogPost>.Failure(BlogPostErrors.SlugAlreadyExists(draft.Slug));
        }
    }

    public async ValueTask<Result<BlogPost>> SetStatusAsync(
        Guid postId,
        int expectedVersion,
        BlogPostStatus status,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await LoadMutablePostAsync(dbContext, postId, cancellationToken);
        if (record is null)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(postId));
        }

        var currentPost = Map(record);
        if (currentPost.Status == status)
        {
            return currentPost.Version == expectedVersion
                ? Result<BlogPost>.Success(currentPost)
                : Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }

        if (record.Version != expectedVersion)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }

        var updatedPost = currentPost.ApplyStatus(status, actorId, now);
        ApplyPost(dbContext, record, updatedPost);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogPost>.Success(Map(record));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }
    }

    public async ValueTask<Result<BlogPost>> PublishAsync(
        Guid postId,
        int expectedVersion,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await LoadMutablePostAsync(dbContext, postId, cancellationToken);
        if (record is null)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(postId));
        }

        var currentPost = Map(record);
        if (currentPost.Status == BlogPostStatus.Published)
        {
            return currentPost.Version == expectedVersion
                ? Result<BlogPost>.Success(currentPost)
                : Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }

        if (currentPost.Version != expectedVersion)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }

        var publishedPost = currentPost.ApplyStatus(BlogPostStatus.Published, actorId, now);
        ApplyPost(dbContext, record, publishedPost);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogPost>.Success(Map(record));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }
    }

    public async ValueTask<Result<BlogPost>> SchedulePublicationAsync(
        Guid postId,
        int expectedVersion,
        BlogPublicationSchedule publicationSchedule,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publicationSchedule);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await LoadMutablePostAsync(dbContext, postId, cancellationToken);
        if (record is null)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(postId));
        }

        if (record.Version != expectedVersion)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }

        var currentPost = Map(record);
        if (!currentPost.TryApplyPublicationSchedule(publicationSchedule, actorId, now, out var updatedPost, out var scheduleError))
        {
            return Result<BlogPost>.Failure(MapScheduleUpdateError(scheduleError));
        }

        if (updatedPost == currentPost)
        {
            return Result<BlogPost>.Success(currentPost);
        }

        ApplyPost(dbContext, record, updatedPost);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogPost>.Success(Map(record));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogPost>.Failure(BlogPostErrors.ConcurrencyConflict(postId));
        }
    }

    public async ValueTask<IReadOnlyCollection<ProcessedBlogPostTransition>> ProcessDueScheduledTransitionsAsync(
        Instant now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var normalizedBatchSize = batchSize > 0
            ? batchSize
            : BlogPostSchedulingDefaults.DefaultProcessingBatchSize;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var duePostIds = await dbContext.PostSchedules
            .AsNoTracking()
            .Where(schedule =>
                (schedule.ScheduledPublishForUtc.HasValue && schedule.ScheduledPublishForUtc <= now.ToDateTimeOffset())
                || (schedule.ScheduledUnpublishForUtc.HasValue && schedule.ScheduledUnpublishForUtc <= now.ToDateTimeOffset()))
            .OrderBy(schedule => schedule.ScheduledPublishForUtc ?? schedule.ScheduledUnpublishForUtc)
            .Select(schedule => schedule.PostId)
            .Take(normalizedBatchSize)
            .ToArrayAsync(cancellationToken);

        var processed = new List<ProcessedBlogPostTransition>(normalizedBatchSize * 2);
        foreach (var postId in duePostIds)
        {
            processed.AddRange(await ProcessDueTransitionsForPostAsync(postId, now, cancellationToken));
        }

        return processed;
    }

    public async ValueTask<Result> RegisterPublishedViewAsync(string slug, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var affectedRows = await dbContext.Posts
            .Where(post => post.Status == BlogPostStatus.Published && post.Slug == slug)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(post => post.ViewCount, post => post.ViewCount + 1),
                cancellationToken);

        return affectedRows == 0
            ? Result.Failure(BlogPostErrors.PublishedPostNotFound(slug))
            : Result.Success();
    }

    private static BlogPost Map(BlogPostRecord record)
    {
        var tagNames = record.Tags
            .Select(tag => tag.TagSlug)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var shareTargets = record.ShareTargets
            .Select(target => target.Target)
            .OrderBy(target => target, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BlogPost(
            record.PostId,
            record.Slug,
            record.Title,
            record.Summary,
            record.Body,
            record.Featured,
            record.Status,
            record.Version,
            record.RevisionNumber,
            record.CategorySlug,
            tagNames,
            new BlogSeoMetadata(record.SeoTitle, record.SeoDescription, record.SeoKeywords),
            shareTargets,
            record.ViewCount,
            record.CreatedUtc,
            record.UpdatedUtc,
            record.UpdatedByActorId,
            record.PublishedUtc,
            record.PublishedByActorId,
            MapPublicationSchedule(record.PublicationSchedule));
    }

    private static BlogPostRecord CreateRecord(BlogPost post)
    {
        var record = new BlogPostRecord
        {
            PostId = post.PostId,
            Slug = post.Slug,
            Title = post.Title,
            Summary = post.Summary,
            Body = post.Body,
            Featured = post.Featured,
            Status = post.Status,
            Version = post.Version,
            RevisionNumber = post.RevisionNumber,
            CategorySlug = post.CategorySlug,
            SeoTitle = post.SeoMetadata.Title,
            SeoDescription = post.SeoMetadata.Description,
            SeoKeywords = post.SeoMetadata.Keywords,
            ViewCount = post.ViewCount,
            CreatedUtc = post.CreatedUtc,
            UpdatedUtc = post.UpdatedUtc,
            UpdatedByActorId = post.UpdatedByActorId,
            PublishedUtc = post.PublishedUtc,
            PublishedByActorId = post.PublishedByActorId,
        };

        ReplaceTags(record, post.TagNames);
        ReplaceShareTargets(record, post.ShareTargets);
        return record;
    }

    private static void ApplyPost(BlogPersistenceDbContext dbContext, BlogPostRecord record, BlogPost post)
    {
        record.Slug = post.Slug;
        record.Title = post.Title;
        record.Summary = post.Summary;
        record.Body = post.Body;
        record.Featured = post.Featured;
        record.Status = post.Status;
        record.Version = post.Version;
        record.RevisionNumber = post.RevisionNumber;
        record.CategorySlug = post.CategorySlug;
        record.SeoTitle = post.SeoMetadata.Title;
        record.SeoDescription = post.SeoMetadata.Description;
        record.SeoKeywords = post.SeoMetadata.Keywords;
        record.ViewCount = post.ViewCount;
        record.CreatedUtc = post.CreatedUtc;
        record.UpdatedUtc = post.UpdatedUtc;
        record.UpdatedByActorId = post.UpdatedByActorId;
        record.PublishedUtc = post.PublishedUtc;
        record.PublishedByActorId = post.PublishedByActorId;

        ReplaceTags(record, post.TagNames);
        ReplaceShareTargets(record, post.ShareTargets);
        ApplyPublicationSchedule(dbContext, record, post.PublicationSchedule, post.UpdatedByActorId, post.UpdatedUtc);
    }

    private static BlogPostRevisionRecord CreateRevision(BlogPost post, string actorId, Instant now)
    {
        return new BlogPostRevisionRecord
        {
            PostId = post.PostId,
            RevisionNumber = post.RevisionNumber,
            PostVersion = post.Version,
            Title = post.Title,
            Summary = post.Summary,
            Body = post.Body,
            Featured = post.Featured,
            CategorySlug = post.CategorySlug,
            TagNamesJson = Serialize(post.TagNames.ToArray()),
            SeoTitle = post.SeoMetadata.Title,
            SeoDescription = post.SeoMetadata.Description,
            SeoKeywords = post.SeoMetadata.Keywords,
            ShareTargetsJson = Serialize(post.ShareTargets.ToArray()),
            CreatedUtc = now,
            CreatedByActorId = actorId,
        };
    }

    private static void ReplaceTags(BlogPostRecord record, IReadOnlyCollection<string> tagNames)
    {
        record.Tags.Clear();
        foreach (var tagName in tagNames)
        {
            record.Tags.Add(new BlogPostTagRecord
            {
                PostId = record.PostId,
                TagSlug = tagName,
            });
        }
    }

    private static void ReplaceShareTargets(BlogPostRecord record, IReadOnlyCollection<string> shareTargets)
    {
        record.ShareTargets.Clear();
        foreach (var target in shareTargets)
        {
            record.ShareTargets.Add(new BlogPostShareTargetRecord
            {
                PostId = record.PostId,
                Target = target,
            });
        }
    }

    private static string Serialize(string[] values)
    {
        return JsonSerializer.Serialize(values, JsonOptions);
    }

    private static bool IsSlugConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    private static Task<BlogPostRecord?> LoadMutablePostAsync(
        BlogPersistenceDbContext dbContext,
        Guid postId,
        CancellationToken cancellationToken)
    {
        return dbContext.Posts
            .Include(post => post.Tags)
            .Include(post => post.ShareTargets)
            .Include(post => post.PublicationSchedule)
            .AsSplitQuery()
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
    }

    private static async Task<Error?> ValidateTaxonomyAsync(
        BlogPersistenceDbContext dbContext,
        string? categorySlug,
        IReadOnlyCollection<string> tagSlugs,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            var categoryExists = await dbContext.Categories
                .AsNoTracking()
                .AnyAsync(category => category.Slug == categorySlug, cancellationToken);

            if (!categoryExists)
            {
                return BlogTaxonomyErrors.CategoryNotFound(categorySlug);
            }
        }

        if (tagSlugs.Count == 0)
        {
            return null;
        }

        var knownTagSlugs = await dbContext.Tags
            .AsNoTracking()
            .Where(tag => tagSlugs.Contains(tag.Slug))
            .Select(tag => tag.Slug)
            .ToArrayAsync(cancellationToken);

        var missingTag = tagSlugs
            .Except(knownTagSlugs, StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return missingTag is null ? null : BlogTaxonomyErrors.TagNotFound(missingTag);
    }

    private async Task<IReadOnlyCollection<ProcessedBlogPostTransition>> ProcessDueTransitionsForPostAsync(
        Guid postId,
        Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await LoadMutablePostAsync(dbContext, postId, cancellationToken);
        if (record?.PublicationSchedule is null)
        {
            return Array.Empty<ProcessedBlogPostTransition>();
        }

        var transitions = new List<ProcessedBlogPostTransition>(2);
        var schedule = record.PublicationSchedule;
        var nowOffset = now.ToDateTimeOffset();
        var currentPost = Map(record);
        var updatedPost = currentPost;

        if (schedule.ScheduledPublishForUtc.HasValue && schedule.ScheduledPublishForUtc <= nowOffset)
        {
            var actorId = schedule.ScheduledPublishByActorId ?? "unknown";
            var effectivePublishUtc = Instant.FromDateTimeOffset(schedule.ScheduledPublishForUtc.Value);

            if (updatedPost.Status != BlogPostStatus.Published)
            {
                updatedPost = updatedPost.ApplyScheduledPublish(actorId, effectivePublishUtc, now);
                transitions.Add(new ProcessedBlogPostTransition(
                    BlogScheduledTransitionKind.Publish,
                    updatedPost,
                    effectivePublishUtc,
                    actorId));
            }
            else
            {
                updatedPost = updatedPost.ClearScheduledPublish(actorId, now);
            }
        }

        if (schedule.ScheduledUnpublishForUtc.HasValue && schedule.ScheduledUnpublishForUtc <= nowOffset)
        {
            var actorId = schedule.ScheduledUnpublishByActorId ?? "unknown";
            var effectiveUnpublishUtc = Instant.FromDateTimeOffset(schedule.ScheduledUnpublishForUtc.Value);

            if (updatedPost.Status == BlogPostStatus.Published)
            {
                updatedPost = updatedPost.ApplyScheduledUnpublish(actorId, now);
                transitions.Add(new ProcessedBlogPostTransition(
                    BlogScheduledTransitionKind.Unpublish,
                    updatedPost,
                    effectiveUnpublishUtc,
                    actorId));
            }
            else
            {
                updatedPost = updatedPost.ClearScheduledUnpublish(actorId, now);
            }
        }

        if (updatedPost == currentPost)
        {
            return Array.Empty<ProcessedBlogPostTransition>();
        }

        ApplyPost(dbContext, record, updatedPost);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return transitions;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Array.Empty<ProcessedBlogPostTransition>();
        }
    }

    private static BlogPublicationSchedule MapPublicationSchedule(BlogPostScheduleRecord? record)
    {
        if (record is null)
        {
            return BlogPublicationSchedule.Empty;
        }

        return new BlogPublicationSchedule(
            MapScheduledPublication(
                record.ScheduledPublishLocalDate,
                record.ScheduledPublishLocalTime,
                record.ScheduledPublishTimeZoneId,
                record.ScheduledPublishLocalTimeResolution,
                record.ScheduledPublishForUtc,
                record.ScheduledPublishByActorId),
            MapScheduledPublication(
                record.ScheduledUnpublishLocalDate,
                record.ScheduledUnpublishLocalTime,
                record.ScheduledUnpublishTimeZoneId,
                record.ScheduledUnpublishLocalTimeResolution,
                record.ScheduledUnpublishForUtc,
                record.ScheduledUnpublishByActorId));
    }

    private static BlogScheduledPublication? MapScheduledPublication(
        LocalDate? localDate,
        LocalTime? localTime,
        string? timeZoneId,
        string? localTimeResolution,
        DateTimeOffset? scheduledForUtc,
        string? scheduledByActorId)
    {
        if (!localDate.HasValue || !localTime.HasValue || string.IsNullOrWhiteSpace(timeZoneId) || !scheduledForUtc.HasValue)
        {
            return null;
        }

        return new BlogScheduledPublication(
            localDate.Value,
            localTime.Value,
            timeZoneId,
            localTimeResolution ?? BlogPostLocalTimeResolutions.Exact,
            Instant.FromDateTimeOffset(scheduledForUtc.Value),
            scheduledByActorId ?? "unknown");
    }

    private static void ApplyPublicationSchedule(
        BlogPersistenceDbContext dbContext,
        BlogPostRecord record,
        BlogPublicationSchedule publicationSchedule,
        string actorId,
        Instant now)
    {
        if (publicationSchedule.Publish is null && publicationSchedule.Unpublish is null)
        {
            if (record.PublicationSchedule is not null)
            {
                dbContext.PostSchedules.Remove(record.PublicationSchedule);
                record.PublicationSchedule = null;
            }

            return;
        }

        var schedule = record.PublicationSchedule;
        if (schedule is null)
        {
            schedule = new BlogPostScheduleRecord { PostId = record.PostId };
            dbContext.PostSchedules.Add(schedule);
            record.PublicationSchedule = schedule;
        }

        ApplyScheduledPublication(
            publicationSchedule.Publish,
            (target, localDate) => target.ScheduledPublishLocalDate = localDate,
            (target, localTime) => target.ScheduledPublishLocalTime = localTime,
            (target, timeZoneId) => target.ScheduledPublishTimeZoneId = timeZoneId,
            (target, localTimeResolution) => target.ScheduledPublishLocalTimeResolution = localTimeResolution,
            (target, scheduledForUtc) => target.ScheduledPublishForUtc = scheduledForUtc,
            (target, scheduledByActorId) => target.ScheduledPublishByActorId = scheduledByActorId,
            schedule);

        ApplyScheduledPublication(
            publicationSchedule.Unpublish,
            (target, localDate) => target.ScheduledUnpublishLocalDate = localDate,
            (target, localTime) => target.ScheduledUnpublishLocalTime = localTime,
            (target, timeZoneId) => target.ScheduledUnpublishTimeZoneId = timeZoneId,
            (target, localTimeResolution) => target.ScheduledUnpublishLocalTimeResolution = localTimeResolution,
            (target, scheduledForUtc) => target.ScheduledUnpublishForUtc = scheduledForUtc,
            (target, scheduledByActorId) => target.ScheduledUnpublishByActorId = scheduledByActorId,
            schedule);

        schedule.UpdatedUtc = now;
        schedule.UpdatedByActorId = actorId;
    }

    private static void ApplyScheduledPublication(
        BlogScheduledPublication? publication,
        Action<BlogPostScheduleRecord, LocalDate?> setLocalDate,
        Action<BlogPostScheduleRecord, LocalTime?> setLocalTime,
        Action<BlogPostScheduleRecord, string?> setTimeZoneId,
        Action<BlogPostScheduleRecord, string?> setLocalTimeResolution,
        Action<BlogPostScheduleRecord, DateTimeOffset?> setScheduledForUtc,
        Action<BlogPostScheduleRecord, string?> setScheduledByActorId,
        BlogPostScheduleRecord target)
    {
        if (publication is null)
        {
            setLocalDate(target, null);
            setLocalTime(target, null);
            setTimeZoneId(target, null);
            setLocalTimeResolution(target, null);
            setScheduledForUtc(target, null);
            setScheduledByActorId(target, null);
            return;
        }

        setLocalDate(target, publication.ScheduledLocalDate);
        setLocalTime(target, publication.ScheduledLocalTime);
        setTimeZoneId(target, publication.TimeZoneId);
        setLocalTimeResolution(target, publication.LocalTimeResolution);
        setScheduledForUtc(target, publication.ScheduledForUtc.ToDateTimeOffset());
        setScheduledByActorId(target, publication.ScheduledByActorId);
    }

    private static Error MapScheduleUpdateError(BlogPublicationScheduleUpdateError error)
    {
        return error switch
        {
            BlogPublicationScheduleUpdateError.PublishAlreadyPublished => BlogPostSchedulingErrors.PublishAlreadyPublished(),
            BlogPublicationScheduleUpdateError.UnpublishRequiresPublishedState => BlogPostSchedulingErrors.UnpublishRequiresPublishedState(),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown blog publication schedule error."),
        };
    }
}
