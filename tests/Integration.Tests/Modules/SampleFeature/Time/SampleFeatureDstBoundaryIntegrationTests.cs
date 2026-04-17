using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Authorization;
using SampleFeature.PublicContracts.Events;

namespace Integration.Tests.Modules.SampleFeature.Time;

public sealed class SampleFeatureDstBoundaryIntegrationTests
{
    [Xunit.Theory]
    [Xunit.MemberData(nameof(BoundaryCases))]
    public async Task SampleFeaturePreservesPublishedInstantsAcrossDstBoundaryCasesAsync(string caseName, Instant publishedAt)
    {
        var fakeClock = new FakeClock(publishedAt);
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();

        await using var writerApplication = await PostgresBackedApiApplication.StartAsync(
            postgres.GetConnectionString(),
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton<BuildingBlocks.Domain.Time.IClock>(fakeClock);
            });

        var announcementId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        const string publishedByActorId = "identity:user:dst-tester";
        var title = $"DST boundary {caseName}";
        const string body = "Persist this instant exactly through the module write and outbox publication flow.";

        await using (var scope = writerApplication.App.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<ISampleAnnouncementWriter>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxPublisher>();

            await writer.WriteAsync(
                new PublishedSampleAnnouncement(
                    announcementId,
                    title,
                    body,
                    publishedAt,
                    publishedByActorId),
                CancellationToken.None);

            await publisher.PublishAsync(
                new IntegrationEventOutboxPublishRequest(
                    SampleFeatureModuleInfo.ModuleKey,
                    new SampleAnnouncementPublishedEventV1(
                        eventId,
                        publishedAt,
                        announcementId,
                        title,
                        body,
                        publishedByActorId)),
                CancellationToken.None);

            var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await outboxDispatcher.DispatchAvailableAsync(CancellationToken.None);
            Xunit.Assert.Equal(1, dispatched);
        }

        Xunit.Assert.Equal(publishedAt.ToDateTimeOffset(), await ReadPersistedPublishedUtcAsync(postgres.GetConnectionString(), announcementId));
    }

    public static IEnumerable<object[]> BoundaryCases()
    {
        yield return ["spring-forward-gap-lenient", DstRegressionCases.ResolveSpringForwardGapLater().ToInstant()];
        yield return ["fall-back-earlier-occurrence", DstRegressionCases.ResolveFallBackEarlier().ToInstant()];
        yield return ["fall-back-later-occurrence", DstRegressionCases.ResolveFallBackLater().ToInstant()];
    }

    private static async Task<DateTimeOffset> ReadPersistedPublishedUtcAsync(string connectionString, Guid announcementId)
    {
        const string sql = """
            SELECT published_utc
            FROM sample_feature.announcements
            WHERE announcement_id = @announcementId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcementId);

        var scalar = await command.ExecuteScalarAsync();
        return scalar switch
        {
            DateTimeOffset publishedUtc => publishedUtc,
            DateTime publishedUtc => new DateTimeOffset(DateTime.SpecifyKind(publishedUtc, DateTimeKind.Utc)),
            null => throw new InvalidOperationException($"SampleFeature announcement '{announcementId}' was not found."),
            _ => throw new InvalidOperationException(
                $"SampleFeature announcement '{announcementId}' returned an unexpected published_utc value of type '{scalar.GetType().FullName}'.")
        };
    }
}
