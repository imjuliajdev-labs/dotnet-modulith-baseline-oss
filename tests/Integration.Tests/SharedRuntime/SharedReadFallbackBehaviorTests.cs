// Shared-read behavior test: construct each adapter directly with a hand-rolled upstream that
// throws a transient exception so the assertion stays focused on the declared fallback policy.
// A full Postgres-backed host startup would overwhelm a pure fallback-path check that has no
// database state and no DI-composition behavior to verify.

using System.Net.Http;
using Admin.Application.SharedReads;
using Blog.Application.Scheduling;
using Blog.Application.SharedReads;
using BuildingBlocks.Application.SharedReads;
using Identity.PublicContracts.Queries;
using KnowledgeBase.PublicContracts.Queries;
using SampleFeature.Application.Scheduling;
using SampleFeature.Application.SharedReads;

namespace Integration.Tests.SharedRuntime;

public sealed class SharedReadFallbackBehaviorTests
{
    [Xunit.Fact]
    public async Task BlogIdentityTimeZoneReaderReturnsDeclaredFailureWhenUpstreamThrowsTransient()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(BlogIdentityTimeZoneReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.Fail, policy.FallbackBehavior);

        var reader = new BlogIdentityTimeZoneReader(new ThrowingTimeZonePreferenceQueryService(new HttpRequestException("upstream down")));

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Xunit.Assert.True(result.IsFailure);
        Xunit.Assert.Equal(BlogPostSchedulingErrors.TimeZoneReadUnavailable().Code, result.Error.Code);
    }

    [Xunit.Fact]
    public async Task SampleFeatureIdentityTimeZoneReaderReturnsDeclaredFailureWhenUpstreamThrowsTransient()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(SampleFeatureIdentityTimeZoneReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.Fail, policy.FallbackBehavior);

        var reader = new SampleFeatureIdentityTimeZoneReader(new ThrowingTimeZonePreferenceQueryService(new TimeoutException("upstream slow")));

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Xunit.Assert.True(result.IsFailure);
        Xunit.Assert.Equal(SampleAnnouncementSchedulingErrors.CurrentActorTimeZoneUnavailable().Code, result.Error.Code);
    }

    [Xunit.Fact]
    public async Task AdminKnowledgeBaseGuidanceReaderReturnsEmptyFallbackWhenUpstreamThrowsTransient()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(AdminKnowledgeBaseGuidanceReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.ReturnFallback, policy.FallbackBehavior);

        var reader = new AdminKnowledgeBaseGuidanceReader(new ThrowingKnowledgeBaseQueryService(new HttpRequestException("upstream down")));

        var result = await reader.ListAsync(5, CancellationToken.None);

        Xunit.Assert.NotNull(result);
        Xunit.Assert.Empty(result);
    }

    private sealed class ThrowingTimeZonePreferenceQueryService : IIdentityTimeZonePreferenceQueryService
    {
        private readonly Exception _exception;

        public ThrowingTimeZonePreferenceQueryService(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class ThrowingKnowledgeBaseQueryService : IKnowledgeBasePublishedEntryQueryService
    {
        private readonly Exception _exception;

        public ThrowingKnowledgeBaseQueryService(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
}
