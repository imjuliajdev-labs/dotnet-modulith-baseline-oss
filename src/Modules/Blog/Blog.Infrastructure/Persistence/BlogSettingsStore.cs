using Blog.Application.Settings;
using Blog.Infrastructure.Configuration;
using BuildingBlocks.Application.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Blog.Infrastructure.Persistence;

internal sealed class BlogSettingsStore : IBlogSettingsStore
{
    private readonly IDbContextFactory<BlogPersistenceDbContext> _dbContextFactory;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly BlogInfrastructureOptions _options;

    public BlogSettingsStore(
        IDbContextFactory<BlogPersistenceDbContext> dbContextFactory,
        BuildingBlocks.Domain.Time.IClock clock,
        IOptions<BlogInfrastructureOptions> options)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async ValueTask<BlogModuleSettings> GetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await GetOrCreateAsync(dbContext, cancellationToken);
        return Map(record);
    }

    public async ValueTask<Result<BlogModuleSettings>> UpdateAsync(
        int expectedVersion,
        string operatorSummary,
        int previewLimit,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await GetOrCreateAsync(dbContext, cancellationToken);

        if (record.Version != expectedVersion)
        {
            return Result<BlogModuleSettings>.Failure(BlogSettingsErrors.ConcurrencyConflict());
        }

        record.OperatorSummary = operatorSummary;
        record.PreviewLimit = previewLimit;
        record.Version++;
        record.UpdatedUtc = now;
        record.UpdatedByActorId = actorId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogModuleSettings>.Failure(BlogSettingsErrors.ConcurrencyConflict());
        }

        return Result<BlogModuleSettings>.Success(Map(record));
    }

    private async Task<BlogSettingsRecord> GetOrCreateAsync(BlogPersistenceDbContext dbContext, CancellationToken cancellationToken)
    {
        var record = await dbContext.Settings.SingleOrDefaultAsync(
            candidate => candidate.SettingsKey == BlogPersistenceDefaults.SettingsRowKey,
            cancellationToken);

        if (record is not null)
        {
            return record;
        }

        var now = _clock.GetCurrentInstant();
        record = new BlogSettingsRecord
        {
            SettingsKey = BlogPersistenceDefaults.SettingsRowKey,
            OperatorSummary = _options.Experience.Summary,
            PreviewLimit = _options.Operations.PreviewLimit,
            Version = 1,
            UpdatedUtc = now,
            UpdatedByActorId = "system:blog-bootstrap"
        };

        dbContext.Settings.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(record).State = EntityState.Detached;
            record = await dbContext.Settings.SingleAsync(
                candidate => candidate.SettingsKey == BlogPersistenceDefaults.SettingsRowKey,
                cancellationToken);
        }

        return record;
    }

    private static BlogModuleSettings Map(BlogSettingsRecord record)
    {
        return new BlogModuleSettings(
            record.OperatorSummary,
            record.PreviewLimit,
            record.Version,
            record.UpdatedUtc,
            record.UpdatedByActorId);
    }
}
