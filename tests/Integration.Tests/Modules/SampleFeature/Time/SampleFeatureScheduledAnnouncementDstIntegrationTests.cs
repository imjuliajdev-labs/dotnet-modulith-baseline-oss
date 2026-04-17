using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Testing.Time;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using SampleFeature.Domain.Announcements;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Scheduling;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.SampleFeature.Time;

public sealed class SampleFeatureScheduledAnnouncementDstIntegrationTests
{
    [Xunit.Theory]
    [Xunit.MemberData(nameof(DstSchedulingCases))]
    public async Task SchedulingEndpointResolvesDstBoundaryCasesThroughPreferredTimeZoneAsync(
        string caseName,
        LocalDateTime localDateTime,
        string expectedResolution,
        Instant expectedScheduledForUtc)
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
        await using var application = await PostgresBackedApiApplication.StartAsync(
            postgres.GetConnectionString(),
            removeHostedServices: ["ScheduledSampleAnnouncementWorker"]);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var signInResult = await SignInAsync(client);


        var authCookie = signInResult.Cookie;
        var antiforgery = await GetAntiforgeryAsync(client, authCookie);
        await UpdatePreferredTimeZoneAsync(client, antiforgery, authCookie, DstRegressionCases.EasternTimeZoneId);

        var scheduleResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements/scheduled",
            new
            {
                title = $"Scheduled {caseName}",
                body = "Resolve this local time through the preferred actor time zone.",
                scheduledLocalDate = localDateTime.Date.ToDateOnly(),
                scheduledLocalTime = ToTimeOnly(localDateTime.TimeOfDay)
            },
            CombineCookies(authCookie, antiforgery.Cookie),
            antiforgery.HeaderName,
            antiforgery.RequestToken,
            $"scheduled-{caseName}");

        Xunit.Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);

        using var scheduleJson = await ReadJsonAsync(scheduleResponse);
        var scheduledAnnouncementId = scheduleJson.RootElement.GetProperty("scheduledAnnouncementId").GetGuid();
        Xunit.Assert.Equal(DstRegressionCases.EasternTimeZoneId, scheduleJson.RootElement.GetProperty("timeZoneId").GetString());
        Xunit.Assert.Equal(expectedResolution, scheduleJson.RootElement.GetProperty("localTimeResolution").GetString());
        Xunit.Assert.Equal(expectedScheduledForUtc.ToDateTimeOffset(), scheduleJson.RootElement.GetProperty("scheduledForUtc").GetDateTimeOffset());

        var scheduledListResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/sample-feature/announcements/scheduled?limit=10", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, scheduledListResponse.StatusCode);

        using (var scheduledListJson = await ReadJsonAsync(scheduledListResponse))
        {
            var scheduledAnnouncement = scheduledListJson.RootElement
                .GetProperty("announcements")
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("scheduledAnnouncementId").GetGuid() == scheduledAnnouncementId);

            var status = scheduledAnnouncement.GetProperty("status").GetString();
            Xunit.Assert.True(status is "pending" or "published");
        }

        var processorClock = new AdjustableClock(expectedScheduledForUtc + Duration.FromMinutes(1));

        await using (var scope = application.App.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IScheduledSampleAnnouncementStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<ISampleAnnouncementPublisher>();
            var leaseId = Guid.NewGuid();
            var now = processorClock.GetCurrentInstant();
            var leased = await store.LeaseDueAsync(
                now,
                leaseId,
                now + SampleAnnouncementSchedulingDefaults.ProcessingLeaseDuration,
                SampleAnnouncementSchedulingDefaults.DefaultProcessingBatchSize,
                CancellationToken.None);

            var scheduled = Xunit.Assert.Single(leased);

            var published = await publisher.PublishAsync(
                new SampleAnnouncementContent(scheduled.Title, scheduled.Body),
                scheduled.ScheduledForUtc,
                scheduled.ScheduledByActorId,
                CancellationToken.None);

            await store.MarkPublishedAsync(
                scheduled.ScheduledAnnouncementId,
                scheduled.LeaseId,
                published.AnnouncementId,
                published.PublishedAt,
                CancellationToken.None);

            var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await outboxDispatcher.DispatchAvailableAsync(CancellationToken.None);
            Xunit.Assert.Equal(1, dispatched);
        }

        var persistedPublishedUtc = await ReadPersistedPublishedUtcByTitleAsync(
            postgres.GetConnectionString(),
            $"Scheduled {caseName}");

        Xunit.Assert.Equal(expectedScheduledForUtc.ToDateTimeOffset(), persistedPublishedUtc);
    }

    [Xunit.Fact]
    public async Task PreferredTimeZoneChangesAffectSubsequentScheduleResolutionAsync()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var signInResult = await SignInAsync(client);


        var authCookie = signInResult.Cookie;
        var antiforgery = await GetAntiforgeryAsync(client, authCookie);

        await UpdatePreferredTimeZoneAsync(client, antiforgery, authCookie, "Etc/UTC");
        var utcResponse = await ScheduleAsync(client, antiforgery, authCookie, "UTC schedule", "Etc/UTC", new LocalDateTime(2026, 11, 1, 1, 30), "utc-schedule");

        await UpdatePreferredTimeZoneAsync(client, antiforgery, authCookie, DstRegressionCases.EasternTimeZoneId);
        var easternResponse = await ScheduleAsync(client, antiforgery, authCookie, "Eastern schedule", DstRegressionCases.EasternTimeZoneId, DstRegressionCases.FallBackAmbiguous.LocalDateTime, "eastern-schedule");

        Xunit.Assert.Equal(Instant.FromUtc(2026, 11, 1, 1, 30).ToDateTimeOffset(), utcResponse.ScheduledForUtc);
        Xunit.Assert.Equal("exact", utcResponse.LocalTimeResolution);
        Xunit.Assert.Equal(DstRegressionCases.ResolveFallBackEarlier().ToInstant().ToDateTimeOffset(), easternResponse.ScheduledForUtc);
        Xunit.Assert.Equal("ambiguous_earlier", easternResponse.LocalTimeResolution);
        Xunit.Assert.NotEqual(utcResponse.ScheduledForUtc, easternResponse.ScheduledForUtc);
    }

    public static IEnumerable<object[]> DstSchedulingCases()
    {
        yield return [
            "spring-forward-gap",
            DstRegressionCases.SpringForwardGap.LocalDateTime,
            SampleAnnouncementLocalTimeResolutions.SkippedForward,
            DstRegressionCases.ResolveSpringForwardGapLater().ToInstant()];

        yield return [
            "fall-back-ambiguous-earlier",
            DstRegressionCases.FallBackAmbiguous.LocalDateTime,
            SampleAnnouncementLocalTimeResolutions.AmbiguousEarlier,
            DstRegressionCases.ResolveFallBackEarlier().ToInstant()];
    }
    private static async Task UpdatePreferredTimeZoneAsync(HttpClient client, AntiforgeryContext antiforgery, string authCookie, string preferredTimeZoneId)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/identity/me/preferences/time-zone",
            new { preferredTimeZoneId },
            CombineCookies(authCookie, antiforgery.Cookie),
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<ScheduledAnnouncementResponse> ScheduleAsync(
        HttpClient client,
        AntiforgeryContext antiforgery,
        string authCookie,
        string title,
        string expectedTimeZoneId,
        LocalDateTime localDateTime,
        string requestKey)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements/scheduled",
            new
            {
                title,
                body = "Schedule through the current actor preference.",
                scheduledLocalDate = localDateTime.Date.ToDateOnly(),
                scheduledLocalTime = ToTimeOnly(localDateTime.TimeOfDay)
            },
            CombineCookies(authCookie, antiforgery.Cookie),
            antiforgery.HeaderName,
            antiforgery.RequestToken,
            requestKey);

        response.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(response);
        return new ScheduledAnnouncementResponse(
            json.RootElement.GetProperty("timeZoneId").GetString() ?? string.Empty,
            json.RootElement.GetProperty("localTimeResolution").GetString() ?? string.Empty,
            json.RootElement.GetProperty("scheduledForUtc").GetDateTimeOffset());
    }
    private static TimeOnly ToTimeOnly(LocalTime localTime)
    {
        return new TimeOnly(localTime.Hour, localTime.Minute, localTime.Second, localTime.Millisecond);
    }
    private static async Task<DateTimeOffset> ReadPersistedPublishedUtcByTitleAsync(string connectionString, string title)
    {
        const string sql = """
            SELECT published_utc
            FROM sample_feature.announcements
            WHERE title = @title;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("title", title);

        var scalar = await command.ExecuteScalarAsync();
        return scalar switch
        {
            DateTimeOffset publishedUtc => publishedUtc,
            DateTime publishedUtc => new DateTimeOffset(DateTime.SpecifyKind(publishedUtc, DateTimeKind.Utc)),
            null => throw new InvalidOperationException($"SampleFeature announcement '{title}' was not found."),
            _ => throw new InvalidOperationException(
                $"SampleFeature announcement '{title}' returned an unexpected published_utc value of type '{scalar.GetType().FullName}'.")
        };
    }

    private sealed record ScheduledAnnouncementResponse(string TimeZoneId, string LocalTimeResolution, DateTimeOffset ScheduledForUtc);
}
