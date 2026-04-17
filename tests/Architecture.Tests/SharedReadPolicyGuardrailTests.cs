namespace Architecture.Tests;

public sealed class SharedReadPolicyGuardrailTests
{
    [Fact]
    public void CrossModuleReadConsumersUseModuleOwnedAdaptersInsteadOfRawProviderContracts()
    {
        if (TryReadSource("src/Modules/Admin/Admin.Application/Queries/ListAdminGuidanceQuery.cs", out var adminGuidanceSource))
        {
            Assert.Contains("IAdminKnowledgeBaseGuidanceReader", adminGuidanceSource, StringComparison.Ordinal);
            Assert.DoesNotContain("IKnowledgeBasePublishedEntryQueryService", adminGuidanceSource, StringComparison.Ordinal);
        }

        if (TryReadSource("src/Modules/Blog/Blog.Application/Scheduling/BlogPostSchedulingContracts.cs", out var blogSchedulingSource))
        {
            Assert.Contains("IBlogIdentityTimeZoneReader", blogSchedulingSource, StringComparison.Ordinal);
            Assert.DoesNotContain("IIdentityTimeZonePreferenceQueryService", blogSchedulingSource, StringComparison.Ordinal);
        }

        if (TryReadSource("src/Modules/SampleFeature/SampleFeature.Application/Scheduling/SampleAnnouncementSchedulingContracts.cs", out var sampleFeatureSchedulingSource))
        {
            Assert.Contains("ISampleFeatureIdentityTimeZoneReader", sampleFeatureSchedulingSource, StringComparison.Ordinal);
            Assert.DoesNotContain("IIdentityTimeZonePreferenceQueryService", sampleFeatureSchedulingSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedReadAdaptersDeclareExplicitPolicies()
    {
        if (TryReadSource("src/Modules/Admin/Admin.Application/SharedReads/AdminKnowledgeBaseGuidanceReader.cs", out var adminReaderSource))
        {
            Assert.Contains("SharedReadPolicy", adminReaderSource, StringComparison.Ordinal);
            Assert.Contains("Timeout: TimeSpan.FromSeconds(1)", adminReaderSource, StringComparison.Ordinal);
            Assert.Contains("SharedReadFallbackBehavior.ReturnFallback", adminReaderSource, StringComparison.Ordinal);
        }

        if (TryReadSource("src/Modules/Blog/Blog.Application/SharedReads/BlogIdentityTimeZoneReader.cs", out var blogTimeZoneReaderSource))
        {
            Assert.Contains("SharedReadPolicy", blogTimeZoneReaderSource, StringComparison.Ordinal);
            Assert.Contains("SharedReadFallbackBehavior.Fail", blogTimeZoneReaderSource, StringComparison.Ordinal);
        }

        if (TryReadSource("src/Modules/SampleFeature/SampleFeature.Application/SharedReads/SampleFeatureIdentityTimeZoneReader.cs", out var sampleFeatureTimeZoneReaderSource))
        {
            Assert.Contains("SharedReadPolicy", sampleFeatureTimeZoneReaderSource, StringComparison.Ordinal);
            Assert.Contains("SharedReadFallbackBehavior.Fail", sampleFeatureTimeZoneReaderSource, StringComparison.Ordinal);
        }
    }

    private static bool TryReadSource(string relativePath, out string source)
    {
        var fullPath = RepositoryFiles.PathFromRoot(relativePath.Split('/'));
        if (!File.Exists(fullPath))
        {
            source = string.Empty;
            return false;
        }

        source = RepositoryFiles.ReadAllText(relativePath);
        return true;
    }
}
