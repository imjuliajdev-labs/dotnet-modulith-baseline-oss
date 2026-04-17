namespace Architecture.Tests;

public sealed class ContractGenerationGuardrailTests
{
    [Fact]
    public void GenerateContractsScriptRequiresExplicitRegenerationAndReviewableDiffs()
    {
        var source = RepositoryFiles.ReadAllText("scripts/Generate-Contracts.ps1");

        Assert.Contains("$openApiRequestParameters = @{", source, StringComparison.Ordinal);
        Assert.Contains("OutFile = $openApiOutputPath", source, StringComparison.Ordinal);
        Assert.Contains("Invoke-WebRequest @openApiRequestParameters", source, StringComparison.Ordinal);
        Assert.Contains("pnpm generate:contracts", source, StringComparison.Ordinal);
        Assert.Contains(
            "git diff --exit-code -- contracts/http/openapi.v1.json web/src/shared/api/generated/contracts.generated.ts",
            source,
            StringComparison.Ordinal);
        Assert.Contains("if ($LASTEXITCODE -ne 0)", source, StringComparison.Ordinal);
        Assert.Contains(
            "throw 'Generated contract artifacts changed. Review and commit contracts/http/openapi.v1.json and web/src/shared/api/generated/contracts.generated.ts.'",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OpenApiSnapshotTestsReadTheCheckedInSnapshotWithoutRewritingIt()
    {
        var source = RepositoryFiles.ReadAllText("tests/Architecture.Tests/OpenApiContractSnapshotTests.cs");

        Assert.Contains("RepositoryFiles.ReadJsonDocument(\"contracts\", \"http\", \"openapi.v1.json\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteAllText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllText", source, StringComparison.Ordinal);
    }
}
