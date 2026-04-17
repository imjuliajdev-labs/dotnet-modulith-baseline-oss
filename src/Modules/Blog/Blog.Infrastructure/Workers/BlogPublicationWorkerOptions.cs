namespace Blog.Infrastructure.Workers;

internal sealed class BlogPublicationWorkerOptions
{
    public const string SectionName = "Modules:Blog:SchedulingWorker";

    public int BatchSize { get; set; } = 10;

    public int PollIntervalMilliseconds { get; set; } = 1000;
}
