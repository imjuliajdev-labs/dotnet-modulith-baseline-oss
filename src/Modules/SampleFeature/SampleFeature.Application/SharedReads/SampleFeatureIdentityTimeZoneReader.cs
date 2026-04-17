using BuildingBlocks.Application.Results;
using BuildingBlocks.Application.SharedReads;
using Identity.PublicContracts.Queries;
using SampleFeature.Application.Scheduling;

namespace SampleFeature.Application.SharedReads;

public interface ISampleFeatureIdentityTimeZoneReader
{
    ValueTask<Result<string>> GetRequiredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken);
}

public sealed class SampleFeatureIdentityTimeZoneReader : ISampleFeatureIdentityTimeZoneReader
{
    internal static readonly SharedReadPolicy Policy = new(
        Name: "sample-feature.identity-time-zone",
        Timeout: TimeSpan.FromSeconds(1),
        FreshnessExpectation: "Current actor time-zone reads must observe the latest committed Identity preference within one request and fail closed on uncertainty.",
        FallbackBehavior: SharedReadFallbackBehavior.Fail);

    private readonly IIdentityTimeZonePreferenceQueryService _queryService;

    public SampleFeatureIdentityTimeZoneReader(IIdentityTimeZonePreferenceQueryService queryService)
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
                return Result<string>.Failure(SampleAnnouncementSchedulingErrors.CurrentActorTimeZoneRequired());
            }

            return SampleAnnouncementScheduleResolver.IsValidTimeZoneId(timeZoneId)
                ? Result<string>.Success(timeZoneId)
                : Result<string>.Failure(SampleAnnouncementSchedulingErrors.CurrentActorTimeZoneInvalid(timeZoneId));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Failure(SampleAnnouncementSchedulingErrors.CurrentActorTimeZoneUnavailable());
        }
        catch (Exception)
        {
            return Result<string>.Failure(SampleAnnouncementSchedulingErrors.CurrentActorTimeZoneUnavailable());
        }
    }
}
