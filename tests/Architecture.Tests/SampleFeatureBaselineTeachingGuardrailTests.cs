using System.Linq;
using System.Reflection;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Infrastructure.Modules;
using SampleFeature.Api;
using SampleFeature.Application.Publishing;

namespace Architecture.Tests;

/// <summary>
/// SampleFeature is the teaching module adopters copy first. These guardrails pin the
/// non-functional patterns (audit writes, options validation, rate limiter contribution)
/// so a future refactor can't quietly teach a weaker shape.
/// </summary>
public sealed class SampleFeatureBaselineTeachingGuardrailTests
{
    /// <summary>
    /// Command handlers that are NOT required to write audit events. Each entry must be
    /// justified in a one-line comment because anything added here weakens the teaching
    /// guarantee that command handlers produce an audit trail.
    /// </summary>
    private static readonly HashSet<string> AuditExemptCommandHandlers = new(StringComparer.Ordinal)
    {
        // Worker-dispatched aggregator that iterates leased scheduled announcements and
        // calls the publisher; the per-announcement audit is the publish command handler's
        // responsibility.
        "ProcessDueScheduledSampleAnnouncementsCommandHandler",
    };

    [Fact]
    public void UserFacingSampleFeatureCommandHandlersDependOnAuditEventWriter()
    {
        var assembly = typeof(PublishSampleAnnouncementCommand).Assembly;

        var handlerTypes = assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .ToArray();

        Assert.NotEmpty(handlerTypes);

        var missing = new List<string>();
        foreach (var handler in handlerTypes)
        {
            if (AuditExemptCommandHandlers.Contains(handler.Name))
            {
                continue;
            }

            var takesAuditWriter = handler
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(IAuditEventWriter)));

            if (!takesAuditWriter)
            {
                missing.Add(handler.FullName ?? handler.Name);
            }
        }

        Assert.True(
            missing.Count == 0,
            "SampleFeature command handlers must take IAuditEventWriter as a constructor dependency " +
            "(or be added to AuditExemptCommandHandlers with justification). Missing: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void SampleFeatureInfrastructureRegistersValidatedWorkerOptions()
    {
        var source = RepositoryFiles.ReadAllText(
            "src/Modules/SampleFeature/SampleFeature.Infrastructure/SampleFeatureInfrastructureServiceCollectionExtensions.cs");

        Assert.Contains("AddOptions<ScheduledSampleAnnouncementWorkerOptions>", source, StringComparison.Ordinal);
        Assert.Contains(".ValidateOnStart()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "configuration.GetSection(\"Modules:SampleFeature:SchedulingWorker\").Get<ScheduledSampleAnnouncementWorkerOptions>()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SampleFeatureModuleImplementsRateLimiterContributor()
    {
        var moduleType = typeof(SampleFeatureModule);

        Assert.Contains(typeof(IModuleRateLimiterContributor), moduleType.GetInterfaces());
    }

    [Fact]
    public void SampleFeatureModuleEndpointsRequireRateLimitingOnMutations()
    {
        var source = RepositoryFiles.ReadAllText(
            "src/Modules/SampleFeature/SampleFeature.Api/SampleFeatureModule.cs");

        Assert.Contains(
            ".RequireRateLimiting(SampleFeatureEndpointPolicies.PublishAnnouncement)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ".RequireRateLimiting(SampleFeatureEndpointPolicies.ScheduleAnnouncement)",
            source,
            StringComparison.Ordinal);
    }
}
