using Blog.Application.SharedReads;
using Identity.PublicContracts.Queries;

namespace Module.UnitTests.Blog.SharedReads;

public sealed class BlogIdentityTimeZoneReaderTests
{
    [Fact]
    public async Task ReturnsConfiguredTimeZoneWhenIdentityProvidesAValidIanaZone()
    {
        var reader = new BlogIdentityTimeZoneReader(new StubTimeZonePreferenceQueryService("Etc/UTC"));

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Etc/UTC", result.Value);
    }

    [Fact]
    public async Task ReturnsServiceUnavailableWhenIdentityReadFails()
    {
        var reader = new BlogIdentityTimeZoneReader(new ThrowingTimeZonePreferenceQueryService());

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("blog.schedule_time_zone_unavailable", result.Error.Code);
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
