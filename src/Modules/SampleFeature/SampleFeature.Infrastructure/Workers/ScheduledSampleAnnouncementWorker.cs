using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using SampleFeature.Application.Authorization;
using SampleFeature.Application.Scheduling;

namespace SampleFeature.Infrastructure.Workers;

internal sealed class ScheduledSampleAnnouncementWorker : ModulePollingBackgroundService
{
    private readonly int _batchSize;
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledSampleAnnouncementWorker(
        IServiceScopeFactory scopeFactory,
        BuildingBlocks.Application.Modules.IModuleExecutionGate moduleExecutionGate,
        ScheduledSampleAnnouncementWorkerOptions options,
        ILogger<ScheduledSampleAnnouncementWorker> logger)
        : base(moduleExecutionGate, NormalizePollInterval(options).ToTimeSpan(), logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _batchSize = NormalizeBatchSize(options);
    }

    protected override string ModuleKey => SampleFeatureModuleInfo.ModuleKey;

    protected override async Task ExecuteEnabledIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new ProcessDueScheduledSampleAnnouncementsCommand(_batchSize), stoppingToken);
    }

    private static Duration NormalizePollInterval(ScheduledSampleAnnouncementWorkerOptions? options)
    {
        var milliseconds = options?.PollIntervalMilliseconds ?? 500;
        return Duration.FromMilliseconds(milliseconds > 0 ? milliseconds : 500);
    }

    private static int NormalizeBatchSize(ScheduledSampleAnnouncementWorkerOptions? options)
    {
        var batchSize = options?.BatchSize ?? 10;
        return batchSize > 0 ? batchSize : 10;
    }
}
