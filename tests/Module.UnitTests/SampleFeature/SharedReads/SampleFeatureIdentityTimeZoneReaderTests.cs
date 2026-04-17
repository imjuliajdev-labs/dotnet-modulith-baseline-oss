using Identity.PublicContracts.Queries;
using SampleFeature.Application.SharedReads;

namespace Module.UnitTests.SampleFeature.SharedReads;

public sealed class SampleFeatureIdentityTimeZoneReaderTests
{
    [Fact]
    public async Task ReturnsConfiguredTimeZoneWhenIdentityProvidesAValidIanaZone()
    {
        var reader = new SampleFeatureIdentityTimeZoneReader(new StubTimeZonePreferenceQueryService("Etc/UTC"));

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Etc/UTC", result.Value);
    }

    [Fact]
    public async Task ReturnsServiceUnavailableWhenIdentityReadFails()
    {
        var reader = new SampleFeatureIdentityTimeZoneReader(new ThrowingTimeZonePreferenceQueryService());

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sample-feature.scheduled_announcement_time_zone_unavailable", result.Error.Code);
    }

    private sealed class StubTimeZonePreferenceQueryService : IIdentityTimeZonePreferenceQueryService
    {
        private readonly string? _timeZoneId;

        public StubTimeZonePreferenceQueryService(string? timeZoneId)
        {
            _timeZoneId = timeZoneId;
        }

        public ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_timeZoneId);
        }
    }

    private sealed class ThrowingTimeZonePreferenceQueryService : IIdentityTimeZonePreferenceQueryService
    {
        public ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Identity read failed.");
        }
    }
}
