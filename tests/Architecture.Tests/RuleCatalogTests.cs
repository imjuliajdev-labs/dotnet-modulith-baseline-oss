namespace Architecture.Tests;

public sealed class RuleCatalogTests
{
    private static readonly string[] AllowedPrimaryEnforcements =
    [
        "Spec And Scaffold Gate",
        "Build Gate",
        "Architecture Gate",
        "Integration Gate",
        "Frontend Gate",
        "Contract Gate",
        "Data Gate",
        "Security And Redaction Gate"
    ];

    [Fact]
    public void RuleCatalogIdsAreUniqueAndContiguous()
    {
        var entries = RepositoryFiles.ReadRuleCatalog();
        var expectedIds = Enumerable.Range(1, entries.Count)
            .Select(index => $"BP-{index:000}")
            .ToArray();

        Assert.Equal(expectedIds, entries.Select(entry => entry.RuleId).ToArray());
    }

    [Fact]
    public void RuleCatalogUsesKnownPrimaryEnforcementValues()
    {
        var entries = RepositoryFiles.ReadRuleCatalog();

        foreach (var entry in entries)
        {
            Assert.Contains(entry.PrimaryEnforcement, AllowedPrimaryEnforcements);
        }
    }

    [Fact]
    public void RuleCatalogEntryCountMatchesBlueprintNonNegotiables()
    {
        var entries = RepositoryFiles.ReadRuleCatalog();
        var nonNegotiables = RepositoryFiles.ReadBlueprintNonNegotiables();

        Assert.Equal(nonNegotiables.Count, entries.Count);
    }

    [Fact]
    public void RuleEnforcementMapCoversAllRuleIds()
    {
        var entries = RepositoryFiles.ReadRuleCatalog();
        var map = RepositoryFiles.ReadRuleEnforcementMap();

        Assert.Equal("1.0", map.SchemaVersion);
        Assert.Equal(
            entries.Select(static entry => entry.RuleId).OrderBy(static id => id, StringComparer.Ordinal),
            map.Rules.Keys.OrderBy(static id => id, StringComparer.Ordinal));

        foreach (var entry in entries)
        {
            Assert.True(map.Rules.TryGetValue(entry.RuleId, out var artifacts), $"Rule id '{entry.RuleId}' is missing from rule enforcement map.");
            Assert.NotNull(artifacts);
            Assert.NotEmpty(artifacts!);
        }
    }

    [Fact]
    public void RuleEnforcementArtifactsExist()
    {
        var map = RepositoryFiles.ReadRuleEnforcementMap();

        foreach (var (ruleId, artifacts) in map.Rules)
        {
            foreach (var artifact in artifacts)
            {
                var fullPath = RepositoryFiles.PathFromRoot(artifact.Split('/'));
                Assert.True(File.Exists(fullPath), $"Rule '{ruleId}' references a missing artifact: {artifact}");
            }
        }
    }

    [Fact]
    public void RuleEnforcementArtifactsUseGovernedExecutableFamiliesOnly()
    {
        var map = RepositoryFiles.ReadRuleEnforcementMap();

        foreach (var (ruleId, artifacts) in map.Rules)
        {
            foreach (var artifact in artifacts)
            {
                Assert.True(
                    RepositoryFiles.IsGovernedRuleEnforcementArtifact(artifact),
                    $"Rule '{ruleId}' references '{artifact}', which is not a governed executable, script, or lint artifact.");
            }
        }
    }

    [Fact]
    public void PlatformSubsystemGuardrailsAreMappedToTheSubsystemOwningRules()
    {
        var map = RepositoryFiles.ReadRuleEnforcementMap();

        Assert.Contains("tests/Architecture.Tests/DispatcherPipelineRegistrationTests.cs", map.Rules["BP-002"]);
        Assert.Contains("tests/Architecture.Tests/PlatformSubsystemGuardrailTests.cs", map.Rules["BP-007"]);
        Assert.Contains("tests/Architecture.Tests/PlatformSubsystemGuardrailTests.cs", map.Rules["BP-019"]);
        Assert.Contains("tests/Architecture.Tests/PlatformSubsystemGuardrailTests.cs", map.Rules["BP-021"]);
    }
}
