namespace SampleFeature.Infrastructure.Workers;

public sealed class ScheduledSampleAnnouncementWorkerOptions
{
    public const string SectionName = "Modules:SampleFeature:SchedulingWorker";

    public int PollIntervalMilliseconds { get; set; } = 500;

    public int BatchSize { get; set; } = 10;
}
