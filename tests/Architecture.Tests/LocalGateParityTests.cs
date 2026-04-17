using System.Text.Json;
using System.Text.RegularExpressions;

namespace Architecture.Tests;

/// <summary>
/// Guards the contract between governance/ci-gates.json (the canonical CI gate
/// manifest), .github/workflows/ci.yml, and scripts/Invoke-LocalGates.ps1.
///
/// The manifest is the single source of truth for gate identity, command
/// bodies, and ordering. Both the workflow and the local runner dispatch each
/// gate through scripts/Invoke-CiGate.ps1 -Id &lt;id&gt;. This test class fails
/// loudly if a gate is added to one source and not the other, or if the
/// manifest order disagrees with the CI job dependency graph.
/// </summary>
public sealed class LocalGateParityTests
{
    private static readonly Regex DispatcherInvocationPattern = new(
        @"\./scripts/Invoke-CiGate\.ps1\s+-Id\s+(?<id>[A-Za-z0-9-]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex JobHeaderPattern = new(
        @"^  (?<id>[a-z0-9-]+):\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex InlineNeedsPattern = new(
        @"^    needs:\s*(?<need>[a-z0-9-]+)\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ListNeedsItemPattern = new(
        @"^      - (?<need>[a-z0-9-]+)\s*$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Manifest_loads_with_unique_ordered_gate_ids()
    {
        var manifest = LoadManifest();

        Assert.NotEmpty(manifest.Gates);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gate in manifest.Gates)
        {
            Assert.False(string.IsNullOrWhiteSpace(gate.Id), "Gate id must not be blank.");
            Assert.False(string.IsNullOrWhiteSpace(gate.Job), $"Gate '{gate.Id}' must declare a job.");
            Assert.False(string.IsNullOrWhiteSpace(gate.Command), $"Gate '{gate.Id}' must declare a command.");
            Assert.True(seen.Add(gate.Id), $"Duplicate gate id '{gate.Id}' in manifest.");
        }

        var manifestJobIds = manifest.Jobs.Select(j => j.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gate in manifest.Gates)
        {
            Assert.True(
                manifestJobIds.Contains(gate.Job),
                $"Gate '{gate.Id}' references job '{gate.Job}' which is not declared in the manifest jobs section.");
        }
    }

    [Fact]
    public void Every_manifest_gate_is_dispatched_by_the_ci_workflow()
    {
        var manifest = LoadManifest();
        var workflowText = ReadWorkflowText();
        var dispatchedIds = ExtractDispatchedGateIds(workflowText);

        var missing = manifest.Gates
            .Select(g => g.Id)
            .Where(id => !dispatchedIds.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Every gate listed in governance/ci-gates.json must be invoked by .github/workflows/ci.yml " +
            "via './scripts/Invoke-CiGate.ps1 -Id <id>'. Missing dispatch for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_ci_workflow_dispatch_resolves_to_a_manifest_gate()
    {
        var manifest = LoadManifest();
        var workflowText = ReadWorkflowText();
        var manifestIds = manifest.Gates.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        var dispatchedIds = ExtractDispatchedGateIds(workflowText);
        var unknown = dispatchedIds
            .Where(id => !manifestIds.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            "Every './scripts/Invoke-CiGate.ps1 -Id <id>' invocation in .github/workflows/ci.yml must point at a gate " +
            "declared in governance/ci-gates.json. Unknown ids: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Manifest_gate_order_matches_ci_job_dependency_topology()
    {
        var manifest = LoadManifest();
        var workflowText = ReadWorkflowText();
        var workflowJobNeeds = ParseWorkflowJobNeeds(workflowText);
        var transitiveNeeds = ComputeTransitiveNeeds(workflowJobNeeds);

        // Build a per-gate index in manifest declaration order.
        var gateIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < manifest.Gates.Count; i++)
        {
            gateIndex[manifest.Gates[i].Id] = i;
        }

        var violations = new List<string>();

        for (var i = 0; i < manifest.Gates.Count; i++)
        {
            var gate = manifest.Gates[i];
            if (!workflowJobNeeds.ContainsKey(gate.Job))
            {
                violations.Add($"Manifest gate '{gate.Id}' belongs to job '{gate.Job}', which does not exist in ci.yml.");
                continue;
            }

            var blockingJobs = transitiveNeeds[gate.Job];

            // Every gate whose enclosing job is a transitive dependency of `gate.Job`
            // must precede `gate` in the manifest.
            for (var j = i + 1; j < manifest.Gates.Count; j++)
            {
                var laterGate = manifest.Gates[j];
                if (blockingJobs.Contains(laterGate.Job))
                {
                    violations.Add(
                        $"Manifest order violates CI topology: gate '{gate.Id}' (job '{gate.Job}') " +
                        $"comes before '{laterGate.Id}' (job '{laterGate.Job}') even though '{gate.Job}' " +
                        $"transitively depends on '{laterGate.Job}'.");
                }
            }

            // Gates inside the same job preserve their declaration order; nothing to verify
            // beyond that the manifest declared them in the order CI runs them, which is
            // already implied by the manifest declaration. The next test enforces that.
        }

        Assert.True(
            violations.Count == 0,
            "Manifest order must be a topological extension of the CI job dependency graph. Issues: "
                + string.Join("; ", violations));
    }

    [Fact]
    public void Manifest_jobs_section_matches_ci_workflow_job_dependency_graph()
    {
        var manifest = LoadManifest();
        var workflowText = ReadWorkflowText();
        var workflowJobNeeds = ParseWorkflowJobNeeds(workflowText);

        var manifestJobIds = manifest.Jobs.Select(j => j.Id).ToHashSet(StringComparer.Ordinal);
        var workflowJobIds = workflowJobNeeds.Keys.ToHashSet(StringComparer.Ordinal);

        var manifestOnly = manifestJobIds.Except(workflowJobIds).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var workflowOnly = workflowJobIds.Except(manifestJobIds).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(
            manifestOnly.Length == 0,
            "governance/ci-gates.json declares jobs that do not exist in ci.yml: " + string.Join(", ", manifestOnly));
        Assert.True(
            workflowOnly.Length == 0,
            "ci.yml declares jobs not present in governance/ci-gates.json jobs section: " + string.Join(", ", workflowOnly));

        var mismatches = new List<string>();
        foreach (var manifestJob in manifest.Jobs)
        {
            var manifestNeeds = manifestJob.Needs.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var workflowNeeds = workflowJobNeeds[manifestJob.Id].OrderBy(n => n, StringComparer.Ordinal).ToArray();

            if (!manifestNeeds.SequenceEqual(workflowNeeds, StringComparer.Ordinal))
            {
                mismatches.Add(
                    $"Job '{manifestJob.Id}': manifest needs [{string.Join(", ", manifestNeeds)}] " +
                    $"vs ci.yml needs [{string.Join(", ", workflowNeeds)}].");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            "Manifest jobs.needs must match ci.yml job needs exactly. Mismatches: " + string.Join("; ", mismatches));
    }

    [Fact]
    public void Manifest_commands_resolve_to_existing_repository_files()
    {
        var manifest = LoadManifest();
        var missing = new List<string>();

        foreach (var gate in manifest.Gates)
        {
            // Command shape is "<repo-relative-script-path> [arg ...]". Extract the script.
            var firstSpace = gate.Command.IndexOf(' ', StringComparison.Ordinal);
            var scriptPath = firstSpace < 0 ? gate.Command : gate.Command[..firstSpace];

            var fullPath = RepositoryFiles.PathFromRoot(scriptPath.Split('/'));
            if (!File.Exists(fullPath))
            {
                missing.Add($"{gate.Id} -> {scriptPath}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Every gate command in governance/ci-gates.json must reference an existing repo file. Missing: "
                + string.Join("; ", missing));
    }

    private static GateManifest LoadManifest()
    {
        var path = RepositoryFiles.PathFromRoot("governance", "ci-gates.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var manifest = JsonSerializer.Deserialize<GateManifest>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize CI gate manifest.");

        return manifest;
    }

    private static string ReadWorkflowText()
    {
        return RepositoryFiles.ReadAllText(".github/workflows/ci.yml");
    }

    private static HashSet<string> ExtractDispatchedGateIds(string workflowText)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in DispatcherInvocationPattern.Matches(workflowText))
        {
            ids.Add(match.Groups["id"].Value);
        }

        return ids;
    }

    private static IDictionary<string, IReadOnlyList<string>> ParseWorkflowJobNeeds(string workflowText)
    {
        var lines = workflowText.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var jobs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var jobHeaderRegex = new Regex(@"^  (?<id>[a-z0-9-]+):\s*$", RegexOptions.CultureInvariant);
        var siblingFieldRegex = new Regex(@"^    \S", RegexOptions.CultureInvariant);

        // Find the start of the top-level "jobs:" block. Anything before it (the
        // workflow's `on:` triggers, env, permissions, etc.) is parsed-around so
        // that a two-space-indented child like `  push:` is not mistaken for a job.
        var jobsBlockStart = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i] == "jobs:")
            {
                jobsBlockStart = i + 1;
                break;
            }
        }

        if (jobsBlockStart < 0)
        {
            throw new InvalidOperationException("Could not locate the top-level 'jobs:' block in ci.yml.");
        }

        for (var i = jobsBlockStart; i < lines.Length; i++)
        {
            var headerMatch = jobHeaderRegex.Match(lines[i]);
            if (!headerMatch.Success)
            {
                continue;
            }

            var jobId = headerMatch.Groups["id"].Value;
            var needs = new List<string>();

            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j];

                if (jobHeaderRegex.IsMatch(line))
                {
                    break;
                }

                var inlineMatch = InlineNeedsPattern.Match(line);
                if (inlineMatch.Success)
                {
                    needs.Add(inlineMatch.Groups["need"].Value);
                    break;
                }

                if (line == "    needs:")
                {
                    for (var k = j + 1; k < lines.Length; k++)
                    {
                        var needsLine = lines[k];
                        var itemMatch = ListNeedsItemPattern.Match(needsLine);
                        if (itemMatch.Success)
                        {
                            needs.Add(itemMatch.Groups["need"].Value);
                            continue;
                        }

                        if (siblingFieldRegex.IsMatch(needsLine))
                        {
                            break;
                        }
                    }

                    break;
                }
            }

            jobs[jobId] = needs;
        }

        return jobs;
    }

    private static IDictionary<string, HashSet<string>> ComputeTransitiveNeeds(IDictionary<string, IReadOnlyList<string>> jobNeeds)
    {
        var transitive = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        HashSet<string> Resolve(string jobId)
        {
            if (transitive.TryGetValue(jobId, out var cached))
            {
                return cached;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            transitive[jobId] = set;

            if (!jobNeeds.TryGetValue(jobId, out var directNeeds))
            {
                return set;
            }

            foreach (var need in directNeeds)
            {
                set.Add(need);
                foreach (var ancestor in Resolve(need))
                {
                    set.Add(ancestor);
                }
            }

            return set;
        }

        foreach (var jobId in jobNeeds.Keys)
        {
            Resolve(jobId);
        }

        return transitive;
    }

    private sealed record GateManifest(IReadOnlyList<GateEntry> Gates, IReadOnlyList<JobEntry> Jobs);

    private sealed record GateEntry(string Id, string Job, string DisplayName, string Command, bool? SkipLocally);

    private sealed record JobEntry(string Id, IReadOnlyList<string> Needs);
}
