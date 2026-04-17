using System.Diagnostics;
using System.Text.Json;

namespace Architecture.Tests;

public sealed class WorkflowGovernanceGuardrailTests
{
    [Fact]
    public void AdoptionValidationUsesTheCanonicalLocalGateRunnerInsteadOfInlineValidationCommands()
    {
        var adoptScript = RepositoryFiles.ReadAllText("scripts/Adopt-Baseline.ps1");

        Assert.Contains("Invoke-LocalGates.ps1", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& dotnet build --configuration Release", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --configuration Release", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& dotnet test tests/Module.UnitTests/Module.UnitTests.csproj --configuration Release", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& dotnet test tests/Integration.Tests/Integration.Tests.csproj --configuration Release", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& pwsh ./scripts/Generate-Contracts.ps1 -Configuration Release", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& pnpm lint", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& pnpm typecheck", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& pnpm test", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& pnpm build", adoptScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& pnpm test:e2e", adoptScript, StringComparison.Ordinal);
    }

    [Fact]
    public void AdoptionValidationProfilesResolveToGovernedExecutionPlans()
    {
        using var nonePlan = InvokeGovernanceCommonJson($"Get-AdoptionValidationProfilePlan -RepositoryRoot '{ToPowerShellLiteral(RepositoryFiles.Root)}' -Profile 'none'");
        using var fastPlan = InvokeGovernanceCommonJson($"Get-AdoptionValidationProfilePlan -RepositoryRoot '{ToPowerShellLiteral(RepositoryFiles.Root)}' -Profile 'fast'");
        using var fullPlan = InvokeGovernanceCommonJson($"Get-AdoptionValidationProfilePlan -RepositoryRoot '{ToPowerShellLiteral(RepositoryFiles.Root)}' -Profile 'full'");
        using var manifest = RepositoryFiles.ReadJsonDocument("governance", "ci-gates.json");

        var manifestGateIds = manifest.RootElement
            .GetProperty("gates")
            .EnumerateArray()
            .Select(gate => gate.GetProperty("id").GetString())
            .Where(static gateId => !string.IsNullOrWhiteSpace(gateId))
            .Cast<string>()
            .ToArray();

        Assert.Equal("none", nonePlan.RootElement.GetProperty("RunnerMode").GetString());
        Assert.Equal(0, nonePlan.RootElement.GetProperty("GateIds").GetArrayLength());

        Assert.Equal("subset", fastPlan.RootElement.GetProperty("RunnerMode").GetString());
        string?[] expectedFastGateIds =
        [
            "spec-governance",
            "dependency-policy",
            "build",
            "backend-unit-tests",
            "architecture-tests",
            "frontend-lint",
            "frontend-typecheck",
            "frontend-build"
        ];

        Assert.Equal(
            (IEnumerable<string?>)expectedFastGateIds,
            fastPlan.RootElement.GetProperty("GateIds").EnumerateArray().Select(static gateId => gateId.GetString()).ToArray());

        Assert.Equal("all", fullPlan.RootElement.GetProperty("RunnerMode").GetString());
        Assert.Equal(
            (IEnumerable<string?>)manifestGateIds,
            fullPlan.RootElement.GetProperty("GateIds").EnumerateArray().Select(static gateId => gateId.GetString()).ToArray());
    }

    [Fact]
    public void AdoptionValidationExportsItsResolvedScratchRootToDownstreamGovernanceScripts()
    {
        var adoptScript = RepositoryFiles.ReadAllText("scripts/Adopt-Baseline.ps1");

        Assert.Contains("DOTNET_MODULITH_SCRATCH_ROOT", adoptScript, StringComparison.Ordinal);
        Assert.Contains("$env:DOTNET_MODULITH_SCRATCH_ROOT = $ScratchRoot", adoptScript, StringComparison.Ordinal);
        Assert.Contains("Join-Path -Path $scratchRoot -ChildPath 'adoption'", adoptScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveAdoptionWrapperPromptsForInputsAndDelegatesToTheSupportedAdoptionScript()
    {
        var wrapper = RepositoryFiles.ReadAllText("scripts/Start-Adoption.ps1");

        Assert.Contains("Read-Host", wrapper, StringComparison.Ordinal);
        Assert.Contains("Application name shown to users", wrapper, StringComparison.Ordinal);
        Assert.Contains("Technical slug", wrapper, StringComparison.Ordinal);
        Assert.Contains("docs/MODULE_GUIDE.md", wrapper, StringComparison.Ordinal);
        Assert.Contains("Optional teaching modules:", wrapper, StringComparison.Ordinal);
        Assert.Contains("Sync-RepositoryWorkingTreeSnapshot", wrapper, StringComparison.Ordinal);
        Assert.Contains("working tree snapshot", wrapper, StringComparison.Ordinal);
        Assert.Contains("scripts/Adopt-Baseline.ps1", wrapper, StringComparison.Ordinal);
        Assert.Contains("adopt-spec.json", wrapper, StringComparison.Ordinal);
        Assert.Contains("git clone --no-local", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet sln", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-AdoptionRenames", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-ModuleRemoval", wrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void AdoptionRenameSweepCoversKnownTextAssetsWithoutStandardManagedExtensions()
    {
        var adoptScript = RepositoryFiles.ReadAllText("scripts/Adopt-Baseline.ps1");

        Assert.Contains(".toml", adoptScript, StringComparison.Ordinal);
        Assert.Contains(".svg", adoptScript, StringComparison.Ordinal);
        Assert.Contains("Dockerfile", adoptScript, StringComparison.Ordinal);
        Assert.Contains("Dockerfile.migrator", adoptScript, StringComparison.Ordinal);
        Assert.Contains("social-preview.svg", adoptScript, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceValidationSmokeCoversTheInteractiveAdoptionWrapper()
    {
        var validateGovernance = RepositoryFiles.ReadAllText("scripts/Validate-Governance.ps1");

        Assert.Contains("scripts/Start-Adoption.ps1", validateGovernance, StringComparison.Ordinal);
        Assert.Contains("-DryRunOnly", validateGovernance, StringComparison.Ordinal);
        Assert.Contains("-NoPrompt", validateGovernance, StringComparison.Ordinal);
        Assert.Contains("governance-smoke-product", validateGovernance, StringComparison.Ordinal);
    }

    [Fact]
    public void AdoptionScratchRootResolutionStaysInsideItsDeclaredBase()
    {
        var repositoryRoot = RepositoryFiles.Root;
        var repositoryRootLiteral = ToPowerShellLiteral(repositoryRoot);
        var repoLocalRoot = InvokeGovernanceCommon($"Resolve-AdoptionScratchRoot -Mode 'repo-local' -Root '.tmp/adoption-tests' -RepositoryRoot '{repositoryRootLiteral}'");
        var osTempRoot = InvokeGovernanceCommon($"Resolve-AdoptionScratchRoot -Mode 'os-temp' -Root 'adoption-tests' -RepositoryRoot '{repositoryRootLiteral}'");
        var repositoryEscapeMessage = InvokeGovernanceCommon($"try {{ Resolve-AdoptionScratchRoot -Mode 'repo-local' -Root '../outside-repo' -RepositoryRoot '{repositoryRootLiteral}' | Out-Null; 'unexpected-success' }} catch {{ $_.Exception.Message }}");
        var tempEscapeMessage = InvokeGovernanceCommon($"try {{ Resolve-AdoptionScratchRoot -Mode 'os-temp' -Root '{ToPowerShellLiteral(Path.GetPathRoot(repositoryRoot) ?? repositoryRoot)}' -RepositoryRoot '{repositoryRootLiteral}' | Out-Null; 'unexpected-success' }} catch {{ $_.Exception.Message }}");

        Assert.True(IsSameOrDescendantPath(repositoryRoot, repoLocalRoot), $"Expected '{repoLocalRoot}' to stay under '{repositoryRoot}'.");
        Assert.NotEqual(Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), repoLocalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var tempBasePath = Path.GetFullPath(Path.GetTempPath());
        Assert.True(IsSameOrDescendantPath(tempBasePath, osTempRoot), $"Expected '{osTempRoot}' to stay under '{tempBasePath}'.");
        Assert.NotEqual(tempBasePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), osTempRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        Assert.DoesNotContain("unexpected-success", repositoryEscapeMessage, StringComparison.Ordinal);
        Assert.Contains("repository root", repositoryEscapeMessage, StringComparison.Ordinal);

        Assert.DoesNotContain("unexpected-success", tempEscapeMessage, StringComparison.Ordinal);
        Assert.Contains("operating system temp directory", tempEscapeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AdoptionValidationProfileDocumentationMatchesTheGovernedPlans()
    {
        var adopt = RepositoryFiles.ReadAllText("docs/ADOPT.md");
        var readme = RepositoryFiles.ReadAllText("README.md");
        using var schema = RepositoryFiles.ReadJsonDocument("docs", "schema", "adopt-spec.v1.schema.json");

        Assert.Contains("`none` — skip validation entirely", adopt, StringComparison.Ordinal);
        Assert.Contains("`fast` — run a reviewed manifest-backed subset through `pwsh ./scripts/Invoke-LocalGates.ps1 -Only <gate-id>` (`spec-governance`, `dependency-policy`, `build`, `backend-unit-tests`, `architecture-tests`, `frontend-lint`, `frontend-typecheck`, and `frontend-build`)", adopt, StringComparison.Ordinal);
        Assert.Contains("`full` — run the canonical local gate wall through `pwsh ./scripts/Invoke-LocalGates.ps1`, preserving manifest order and each gate's `skipLocally` default", adopt, StringComparison.Ordinal);

        Assert.Contains("Adoption validation profiles dispatch through the canonical local gate runner", readme, StringComparison.Ordinal);
        Assert.Contains("`full` runs `pwsh ./scripts/Invoke-LocalGates.ps1` in manifest order", readme, StringComparison.Ordinal);
        Assert.Contains("`fast` runs a reviewed manifest-backed subset", readme, StringComparison.Ordinal);
        Assert.Contains("`none` skips validation", readme, StringComparison.Ordinal);

        var profileDescription = schema.RootElement
            .GetProperty("properties")
            .GetProperty("validation")
            .GetProperty("properties")
            .GetProperty("profile")
            .GetProperty("description")
            .GetString();

        Assert.Equal(
            "none skips validation, fast runs a reviewed manifest-backed subset through scripts/Invoke-LocalGates.ps1, and full runs the canonical local gate wall through scripts/Invoke-LocalGates.ps1 while honoring each gate's skipLocally default.",
            profileDescription);
    }

    [Fact]
    public void DurableWorkflowDocsPreferTheCanonicalLocalGateRunner()
    {
        var adopt = RepositoryFiles.ReadAllText("docs/ADOPT.md");
        var declaration = RepositoryFiles.ReadAllText("docs/BASELINE_DECLARATION.md");

        Assert.Contains("Invoke-LocalGates.ps1", adopt, StringComparison.Ordinal);
        Assert.Contains("Invoke-LocalGates.ps1", declaration, StringComparison.Ordinal);

        Assert.DoesNotContain("dotnet test tests/Integration.Tests/Integration.Tests.csproj --configuration Release", adopt, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test tests/Integration.Tests/Integration.Tests.csproj --configuration Release", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributorWorkflowDocsPreferTheCanonicalGateRunner()
    {
        var contributing = RepositoryFiles.ReadAllText("CONTRIBUTING.md");
        var pullRequestTemplate = RepositoryFiles.ReadAllText(".github/pull_request_template.md");

        Assert.Contains("Invoke-LocalGates.ps1", contributing, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id <id>", contributing, StringComparison.Ordinal);
        Assert.DoesNotContain("./scripts/Validate-Governance.ps1", contributing, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --configuration Release", contributing, StringComparison.Ordinal);

        Assert.Contains("Invoke-LocalGates.ps1", pullRequestTemplate, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id <id>", pullRequestTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("./scripts/Validate-Governance.ps1", pullRequestTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void AdoptionWorkflowUsesTheSharedRepoOwnedPromptAndKeepsGateDispatchGuidance()
    {
        var agents = RepositoryFiles.ReadAllText("AGENTS.md");
        var adopt = RepositoryFiles.ReadAllText("docs/ADOPT.md");
        var readme = RepositoryFiles.ReadAllText("README.md");
        var prompt = RepositoryFiles.ReadAllText("prompts/adopt-governed-baseline.md");

        Assert.Contains("prompts/adopt-governed-baseline.md", agents, StringComparison.Ordinal);
        Assert.Contains("prompts/adopt-governed-baseline.md", adopt, StringComparison.Ordinal);
        Assert.Contains("prompts/adopt-governed-baseline.md", readme, StringComparison.Ordinal);
        Assert.Contains("Start-Adoption.ps1", agents, StringComparison.Ordinal);
        Assert.Contains("Start-Adoption.ps1", adopt, StringComparison.Ordinal);
        Assert.Contains("Start-Adoption.ps1", readme, StringComparison.Ordinal);

        Assert.Contains("Adopt-Baseline.ps1", prompt, StringComparison.Ordinal);
        Assert.Contains("Start-Adoption.ps1", prompt, StringComparison.Ordinal);
        Assert.Contains("adopt-spec.example.json", prompt, StringComparison.Ordinal);
        Assert.Contains("human-facing application or product name", prompt, StringComparison.Ordinal);
        Assert.Contains("technical slug", prompt, StringComparison.Ordinal);
        Assert.Contains("docs/MODULE_GUIDE.md", prompt, StringComparison.Ordinal);
        Assert.Contains("SampleFeature", prompt, StringComparison.Ordinal);
        Assert.Contains("KnowledgeBase", prompt, StringComparison.Ordinal);
        Assert.Contains("Invoke-LocalGates.ps1", prompt, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id <id>", prompt, StringComparison.Ordinal);
        Assert.Contains("Platform", prompt, StringComparison.Ordinal);
        Assert.Contains("Identity", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CopilotInstructionsReferenceTheCanonicalRepoOwnedPrompts()
    {
        var copilotInstructions = RepositoryFiles.ReadAllText(".github/copilot-instructions.md");

        Assert.Contains("prompts/add-governed-module.md", copilotInstructions, StringComparison.Ordinal);
        Assert.Contains("prompts/adopt-governed-baseline.md", copilotInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain(".github/prompts/", copilotInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModuleWorkflowUsesGateDispatchForGovernedValidationSteps()
    {
        var addModule = RepositoryFiles.ReadAllText("docs/ADD_MODULE.md");
        var prompt = RepositoryFiles.ReadAllText("prompts/add-governed-module.md");

        Assert.Contains("Invoke-LocalGates.ps1", addModule, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id architecture-tests", addModule, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id integration-tests", addModule, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id contract-generation-and-compatibility", addModule, StringComparison.Ordinal);

        Assert.Contains("Invoke-LocalGates.ps1", prompt, StringComparison.Ordinal);
        Assert.Contains("Invoke-CiGate.ps1 -Id <id>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModuleWorkflowAndSpecSurfaceDependencyHeadroomBeforeCrossModuleReferencesAreIntroduced()
    {
        var addModule = RepositoryFiles.ReadAllText("docs/ADD_MODULE.md");
        var prompt = RepositoryFiles.ReadAllText("prompts/add-governed-module.md");
        var readme = RepositoryFiles.ReadAllText("README.md");
        var moduleSpecExample = RepositoryFiles.ReadAllText("templates/module/module-spec.example.json");
        var scaffoldScript = RepositoryFiles.ReadAllText("scripts/New-Module.ps1");

        Assert.Contains("crossModulePublicContractDependencies", addModule, StringComparison.Ordinal);
        Assert.Contains("Report-ModuleDependencies.ps1", addModule, StringComparison.Ordinal);
        Assert.Contains("crossModulePublicContractDependencies", prompt, StringComparison.Ordinal);
        Assert.Contains("Report-ModuleDependencies.ps1", prompt, StringComparison.Ordinal);
        Assert.Contains("Report-ModuleDependencies.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("\"crossModulePublicContractDependencies\":", moduleSpecExample, StringComparison.Ordinal);
        Assert.Contains("Write-CrossModulePublicContractDependencyPreflight", scaffoldScript, StringComparison.Ordinal);
        Assert.Contains("Report-ModuleDependencies.ps1", scaffoldScript, StringComparison.Ordinal);
    }

    private static JsonDocument InvokeGovernanceCommonJson(string expression)
    {
        var output = InvokeGovernanceCommon($"{expression} | ConvertTo-Json -Depth 8 -Compress");
        return JsonDocument.Parse(output);
    }

    private static string InvokeGovernanceCommon(string expression)
    {
        var scriptPath = RepositoryFiles.PathFromRoot("scripts", "Governance.Common.ps1");
        var command = $". '{ToPowerShellLiteral(scriptPath)}'; {expression}";

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh to evaluate governance workflow helpers.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"PowerShell governance helper command failed with exit code {process.ExitCode}.{Environment.NewLine}Command: {command}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");

        return stdout.Trim();
    }

    private static string ToPowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static bool IsSameOrDescendantPath(string basePath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedBasePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidatePath = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedBasePath, normalizedCandidatePath, comparison))
        {
            return true;
        }

        return normalizedCandidatePath.StartsWith(normalizedBasePath + Path.DirectorySeparatorChar, comparison);
    }
}
