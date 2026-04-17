// Shared-read behavior test: construct the adapter directly with a hand-rolled upstream so the
// assertion stays focused on the declared timeout contract. Booting the full Postgres/web host
// would dominate the wall-clock budget and add unrelated moving parts to a pure timeout-path test.

using System.Diagnostics;
using Blog.Application.Scheduling;
using Blog.Application.SharedReads;
using BuildingBlocks.Application.SharedReads;
using Identity.PublicContracts.Queries;

namespace Integration.Tests.SharedRuntime;

public sealed class SharedReadTimeoutBehaviorTests
{
    [Xunit.Fact]
    public async Task BlogIdentityTimeZoneReaderHonorsDeclaredTimeoutAndReturnsDeclaredFailureFallback()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(BlogIdentityTimeZoneReader));
        Xunit.Assert.Equal("blog.identity-time-zone", policy.Name);
        Xunit.Assert.Equal(TimeSpan.FromSeconds(1), policy.Timeout);
        Xunit.Assert.Equal(SharedReadFallbackBehavior.Fail, policy.FallbackBehavior);

        var blockingUpstream = new BlockingTimeZonePreferenceQueryService(blockFor: TimeSpan.FromSeconds(10));
        var reader = new BlogIdentityTimeZoneReader(blockingUpstream);

        var stopwatch = Stopwatch.StartNew();
        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);
        stopwatch.Stop();

        // Adapter must swallow the cancellation and surface the declared fallback error — not leak OperationCanceledException.
        Xunit.Assert.True(result.IsFailure, "Adapter should fail closed when upstream exceeds the declared timeout.");
        Xunit.Assert.Equal(BlogPostSchedulingErrors.TimeZoneReadUnavailable().Code, result.Error.Code);

        // Must not wait unbounded — allow headroom for CI jitter but stay well under 5s.
        Xunit.Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(3500),
            $"Reader returned in {stopwatch.Elapsed.TotalMilliseconds:F0} ms but the declared timeout is {policy.Timeout.TotalMilliseconds:F0} ms.");

        Xunit.Assert.True(blockingUpstream.WasCancelled, "Inner token derived from the timeout CTS must have been signalled.");
    }

    private sealed class BlockingTimeZonePreferenceQueryService : IIdentityTimeZonePreferenceQueryService
    {
        private readonly TimeSpan _blockFor;

        public BlockingTimeZonePreferenceQueryService(TimeSpan blockFor)
        {
            _blockFor = blockFor;
        }

        public bool WasCancelled { get; private set; }

        public async ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_blockFor, cancellationToken);
                return "Etc/UTC";
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
