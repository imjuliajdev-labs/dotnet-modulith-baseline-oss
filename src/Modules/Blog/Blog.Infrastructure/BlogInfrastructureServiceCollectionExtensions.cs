using BuildingBlocks.Infrastructure.Persistence;
using Blog.Application.Authorization;
using Blog.Application.Posts;
using Blog.Application.Scheduling;
using Blog.Application.Taxonomy;
using Blog.Infrastructure.Queries;
using Blog.Infrastructure.Configuration;
using Blog.Infrastructure.Outbox;
using Blog.Infrastructure.Persistence;
using Blog.Infrastructure.Workers;
using Blog.PublicContracts.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Blog.Application.Settings;

namespace Blog.Infrastructure;

public static class BlogInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBlogInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBlogOutbox();
        services.AddDatabaseMigrationSupport();
        services.AddOptions<BlogInfrastructureOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                configuration.GetSection(BlogInfrastructureOptions.SectionName).Bind(options);
            })
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Experience.Title),
                "Blog configuration requires a non-empty experience title.")
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Experience.Summary),
                "Blog configuration requires a non-empty experience summary.")
            .Validate(
                static options => options.Operations.PreviewLimit > 0,
                "Blog configuration requires a positive operational preview limit.")
            .ValidateOnStart();

        services.AddOptions<BlogPublicationWorkerOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                options.PollIntervalMilliseconds = configuration.GetValue<int?>(
                    $"{BlogPublicationWorkerOptions.SectionName}:{nameof(BlogPublicationWorkerOptions.PollIntervalMilliseconds)}")
                    ?? 1000;
                options.BatchSize = configuration.GetValue<int?>(
                    $"{BlogPublicationWorkerOptions.SectionName}:{nameof(BlogPublicationWorkerOptions.BatchSize)}")
                    ?? BlogPostSchedulingDefaults.DefaultProcessingBatchSize;
            })
            .Validate(
                static options => options.PollIntervalMilliseconds > 0,
                "Blog scheduling worker configuration requires a positive poll interval.")
            .Validate(
                static options => options.BatchSize > 0,
                "Blog scheduling worker configuration requires a positive batch size.")
            .ValidateOnStart();

        services.AddDbContextFactory<BlogPersistenceDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(BlogPersistenceDefaults.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseBlogPersistence(connectionString);
            }
        });

        services.TryAddSingleton<IBlogPostStore, BlogPostStore>();
        services.TryAddSingleton<IBlogPostReadQueries, BlogPostReadQueries>();
        services.TryAddSingleton<IBlogTaxonomyStore, BlogTaxonomyStore>();
        services.TryAddSingleton<IBlogPublishedPostQueryService, BlogPublishedPostQueryService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseMigration, BlogDatabaseMigration>());
        services.TryAddSingleton(static provider => provider.GetRequiredService<IOptions<BlogPublicationWorkerOptions>>().Value);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BlogPublicationWorker>());
        services.TryAddSingleton<IBlogSettingsStore, BlogSettingsStore>();
        services.AddSingleton<ICommandTransactionParticipant>(new BlogCommandTransactionParticipant(BlogModuleInfo.ModuleKey));

        return services;
    }
}

internal sealed record BlogCommandTransactionParticipant(string ModuleKey) : ICommandTransactionParticipant;
