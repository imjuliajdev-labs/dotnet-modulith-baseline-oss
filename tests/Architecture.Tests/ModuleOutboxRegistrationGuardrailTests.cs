namespace Architecture.Tests;

/// <summary>
/// The outbox registration path is one place where the baseline must stay uniform:
/// a bespoke wrapper that silently swaps in a NoOp store is the opposite of the
/// fail-fast posture the rest of the baseline enforces. These guardrails keep every
/// module on the canonical <c>AddPostgresIntegrationEventOutbox</c> helper and
/// forbid any NoOp outbox store from re-appearing in production code.
/// </summary>
public sealed class ModuleOutboxRegistrationGuardrailTests
{
    private static readonly string[] ModuleRegistrationFiles =
    [
        "src/Modules/Blog/Blog.Infrastructure/Outbox/BlogOutboxRegistration.cs",
        "src/Modules/KnowledgeBase/KnowledgeBase.Infrastructure/Outbox/KnowledgeBaseOutboxRegistration.cs",
        "src/Modules/SampleFeature/SampleFeature.Infrastructure/Outbox/SampleFeatureOutboxRegistration.cs",
    ];

    [Fact]
    public void EveryModuleOutboxRegistrationFileCallsAddPostgresIntegrationEventOutbox()
    {
        var violations = new List<string>();

        foreach (var relativePath in ModuleRegistrationFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            if (!source.Contains("AddPostgresIntegrationEventOutbox(", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: must call AddPostgresIntegrationEventOutbox(...) as the single outbox registration path.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void NoOpOutboxStoresMayNotAppearInProductionSource()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src");
        var violations = new List<string>();

        foreach (var relativePath in sourceFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);

            if (source.Contains("NoOp", StringComparison.Ordinal)
                && source.Contains("IIntegrationEventOutboxStore", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: production code must not declare a NoOp IIntegrationEventOutboxStore — outbox misconfiguration is a fail-fast condition.");
            }
        }

        Assert.Empty(violations);
    }
}
