using Blog.Application.Scheduling;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Application.SharedReads;
using Identity.PublicContracts.Queries;

namespace Blog.Application.SharedReads;

public interface IBlogIdentityTimeZoneReader
{
    ValueTask<Result<string>> GetRequiredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken);
}

public sealed class BlogIdentityTimeZoneReader : IBlogIdentityTimeZoneReader
{
    internal static readonly SharedReadPolicy Policy = new(
        Name: "blog.identity-time-zone",
        Timeout: TimeSpan.FromSeconds(1),
        FreshnessExpectation: "Current actor time-zone reads must reflect committed Identity preferences within one request and fail closed on uncertainty.",
        FallbackBehavior: SharedReadFallbackBehavior.Fail);

    private readonly IIdentityTimeZonePreferenceQueryService _queryService;

    public BlogIdentityTimeZoneReader(IIdentityTimeZonePreferenceQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public async ValueTask<Result<string>> GetRequiredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Policy.Timeout);

        try
        {
            var timeZoneId = await _queryService.GetPreferredTimeZoneIdAsync(actorId, timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                return Result<string>.Failure(BlogPostSchedulingErrors.TimeZoneRequired());
            }

            return BlogPublicationScheduleResolver.IsValidTimeZoneId(timeZoneId)
                ? Result<string>.Success(timeZoneId)
                : Result<string>.Failure(BlogPostSchedulingErrors.InvalidTimeZone(timeZoneId));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Failure(BlogPostSchedulingErrors.TimeZoneReadUnavailable());
        }
        catch (Exception)
        {
            return Result<string>.Failure(BlogPostSchedulingErrors.TimeZoneReadUnavailable());
        }
    }
}
