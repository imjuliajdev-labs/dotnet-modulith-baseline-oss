using Blog.Application.Authorization;
using Blog.Application.Scheduling;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Blog.Infrastructure.Workers;

internal sealed class BlogPublicationWorker : ModulePollingBackgroundService
{
    private readonly int _batchSize;
    private readonly IServiceScopeFactory _scopeFactory;

    public BlogPublicationWorker(
        IServiceScopeFactory scopeFactory,
        BuildingBlocks.Application.Modules.IModuleExecutionGate moduleExecutionGate,
        BlogPublicationWorkerOptions options,
        ILogger<BlogPublicationWorker> logger)
        : base(moduleExecutionGate, ResolvePollInterval(options).ToTimeSpan(), logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _batchSize = ResolveBatchSize(options);
    }

    protected override string ModuleKey => BlogModuleInfo.ModuleKey;

    protected override async Task ExecuteEnabledIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new ProcessDueBlogPostSchedulesCommand(_batchSize), stoppingToken);
    }

    private static Duration ResolvePollInterval(BlogPublicationWorkerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.PollIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.PollIntervalMilliseconds), options.PollIntervalMilliseconds, "Poll interval must be positive.");
        }

        return Duration.FromMilliseconds(options.PollIntervalMilliseconds);
    }

    private static int ResolveBatchSize(BlogPublicationWorkerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.BatchSize), options.BatchSize, "Batch size must be positive.");
        }

        return options.BatchSize;
    }
}
