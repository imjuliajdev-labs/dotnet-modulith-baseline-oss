// BP-034 chaos coverage. SharedReadFallbackBehaviorTests already proves that
// the AdminKnowledgeBaseGuidanceReader returns its empty fallback when the
// upstream throws synchronously. SharedReadTimeoutBehaviorTests already proves
// that the BlogIdentityTimeZoneReader honors its declared timeout when the
// upstream blocks (Fail mode).
//
// What's left: prove the timeout machinery itself fires for the
// ReturnFallback policy variant. The runbook calls this out specifically
// because the throwing-upstream path and the blocking-upstream path exercise
// different code edges — `try { await Task.Delay(_blockFor, ct); }
// catch (OperationCanceledException) { ... }` versus `throw new ...()`.
//
// This test wraps the real cross-module shared-read upstream contract with a
// latency-injecting fake that respects the cancellation token, mirroring the
// blocking pattern from SharedReadTimeoutBehaviorTests but for the
// ReturnFallback policy adapter.

using System.Diagnostics;
using Admin.Application.SharedReads;
using BuildingBlocks.Application.SharedReads;
using KnowledgeBase.PublicContracts.Queries;
using Integration.Tests.SharedRuntime;

namespace Integration.Tests.ChaosScenarios;

public sealed class SharedReadChaosTests
{
    [Xunit.Fact]
    public async Task AdminKnowledgeBaseGuidanceReaderTimesOutAndReturnsEmptyFallbackWithinDeclaredBudget()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(AdminKnowledgeBaseGuidanceReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.ReturnFallback, policy.FallbackBehavior);
        Xunit.Assert.True(policy.Timeout > TimeSpan.Zero, "AdminKnowledgeBaseGuidanceReader must declare a positive timeout.");

        var blockingUpstream = new BlockingKnowledgeBaseQueryService(blockFor: TimeSpan.FromSeconds(10));
        var reader = new AdminKnowledgeBaseGuidanceReader(blockingUpstream);

        var stopwatch = Stopwatch.StartNew();
        var result = await reader.ListAsync(5, CancellationToken.None);
        stopwatch.Stop();

        // Adapter must swallow the cancellation and surface the declared empty fallback.
        Xunit.Assert.NotNull(result);
        Xunit.Assert.Empty(result);

        // Must not wait unbounded — cap at the declared timeout plus generous CI jitter,
        // safely below the 10s upstream block.
        var allowedBudget = policy.Timeout + TimeSpan.FromMilliseconds(2500);
        Xunit.Assert.True(
            stopwatch.Elapsed < allowedBudget,
            $"Reader returned in {stopwatch.Elapsed.TotalMilliseconds:F0} ms but the declared timeout budget is {allowedBudget.TotalMilliseconds:F0} ms.");

        Xunit.Assert.True(blockingUpstream.WasCancelled, "Inner token derived from the timeout CTS must have been signalled.");
    }

    private sealed class BlockingKnowledgeBaseQueryService : IKnowledgeBasePublishedEntryQueryService
    {
        private readonly TimeSpan _blockFor;

        public BlockingKnowledgeBaseQueryService(TimeSpan blockFor)
        {
            _blockFor = blockFor;
        }

        public bool WasCancelled { get; private set; }

        public async ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_blockFor, cancellationToken);
                return Array.Empty<KnowledgeBasePublishedEntryReadModel>();
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
