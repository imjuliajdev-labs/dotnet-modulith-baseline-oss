using System.Reflection;
using System.Text.RegularExpressions;
using BuildingBlocks.Domain.Contracts;

namespace Architecture.Tests;

public sealed class HttpContractLifecycleGuardrailTests
{
    private static readonly Regex VersionSuffixPattern = new(@"V\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void EveryVersionedHttpContractDtoDeclaresLifecycleMetadata()
    {
        var missing = DiscoverVersionedHttpContracts()
            .Where(static type => type.GetCustomAttribute<ContractLifecycleAttribute>() is null)
            .Select(static type => type.FullName)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Versioned HTTP contract DTOs missing [ContractLifecycle]: {string.Join(", ", missing)}");
    }

    [Fact]
    public void HttpContractLifecycleDatesAreIso8601LocalDates()
    {
        foreach (var (type, lifecycle) in EnumerateLifecycles())
        {
            Assert.True(
                ContractLifecyclePolicy.TryParseIsoDate(lifecycle.IntroducedOn, out _),
                $"{type.FullName}: IntroducedOn '{lifecycle.IntroducedOn}' is not an ISO-8601 yyyy-MM-dd date.");

            if (lifecycle.RetireOn is not null)
            {
                Assert.True(
                    ContractLifecyclePolicy.TryParseIsoDate(lifecycle.RetireOn, out _),
                    $"{type.FullName}: RetireOn '{lifecycle.RetireOn}' is not an ISO-8601 yyyy-MM-dd date.");
            }
        }
    }

    [Fact]
    public void SupersededHttpContractsDeclareReplacementAndRetireDate()
    {
        foreach (var (type, lifecycle) in EnumerateLifecycles().Where(static pair => pair.Lifecycle.Status == ContractLifecycleStatus.Superseded))
        {
            Assert.True(
                lifecycle.SupersededBy is not null,
                $"{type.FullName}: status is Superseded but SupersededBy is not set.");
            Assert.False(
                string.IsNullOrWhiteSpace(lifecycle.RetireOn),
                $"{type.FullName}: status is Superseded but RetireOn is not set.");
        }
    }

    [Fact]
    public void ActiveHttpContractsDoNotDeclareSupersedeMetadata()
    {
        foreach (var (type, lifecycle) in EnumerateLifecycles().Where(static pair => pair.Lifecycle.Status == ContractLifecycleStatus.Active))
        {
            Assert.True(
                lifecycle.SupersededBy is null,
                $"{type.FullName}: status is Active but SupersededBy is set to {lifecycle.SupersededBy?.FullName}.");
            Assert.True(
                lifecycle.RetireOn is null,
                $"{type.FullName}: status is Active but RetireOn is set to {lifecycle.RetireOn}.");
        }
    }

    [Fact]
    public void SupersedeChainTargetsAreVersionedHttpContracts()
    {
        var registry = DiscoverVersionedHttpContracts().ToHashSet();

        foreach (var (type, lifecycle) in EnumerateLifecycles().Where(static pair => pair.Lifecycle.SupersededBy is not null))
        {
            var successor = lifecycle.SupersededBy!;

            Assert.True(
                registry.Contains(successor),
                $"{type.FullName}: SupersededBy '{successor.FullName}' is not a registered versioned HTTP contract under *.Api.Contracts.");
            Assert.NotEqual(type, successor);
        }
    }

    [Fact]
    public void SupersededHttpContractsRetireWithinCompatibilityWindowOfSuccessor()
    {
        foreach (var (type, lifecycle) in EnumerateLifecycles().Where(static pair => pair.Lifecycle.Status == ContractLifecycleStatus.Superseded))
        {
            var successor = lifecycle.SupersededBy!;
            var successorLifecycle = successor.GetCustomAttribute<ContractLifecycleAttribute>();
            Assert.NotNull(successorLifecycle);

            Assert.True(ContractLifecyclePolicy.TryParseIsoDate(lifecycle.IntroducedOn, out var introducedOn));
            Assert.True(ContractLifecyclePolicy.TryParseIsoDate(lifecycle.RetireOn!, out var retireOn));
            Assert.True(ContractLifecyclePolicy.TryParseIsoDate(successorLifecycle!.IntroducedOn, out var successorIntroducedOn));

            Assert.True(
                retireOn > introducedOn,
                $"{type.FullName}: RetireOn ({retireOn:O}) must be after IntroducedOn ({introducedOn:O}).");
            Assert.True(
                retireOn > successorIntroducedOn,
                $"{type.FullName}: RetireOn ({retireOn:O}) must be after successor IntroducedOn ({successorIntroducedOn:O}).");

            var maxRetireOn = successorIntroducedOn.AddDays(ContractLifecyclePolicy.MaxCompatibilityWindowDays);
            Assert.True(
                retireOn <= maxRetireOn,
                $"{type.FullName}: RetireOn ({retireOn:O}) exceeds {ContractLifecyclePolicy.MaxCompatibilityWindowDays}-day compatibility window after successor IntroducedOn ({successorIntroducedOn:O}); max allowed {maxRetireOn:O}.");
        }
    }

    [Fact]
    public void NoSupersededHttpContractHasExceededItsRetirementDate()
    {
        var today = DateOnly.FromDateTime(NodaTime.SystemClock.Instance.GetCurrentInstant().ToDateTimeUtc());

        var expired = EnumerateLifecycles()
            .Where(pair => ContractLifecyclePolicy.IsPastRetirementDeadline(pair.Lifecycle, today))
            .Select(pair => $"{pair.Type.FullName} (RetireOn={pair.Lifecycle.RetireOn})")
            .ToArray();

        Assert.True(
            expired.Length == 0,
            $"HTTP contract DTOs past their retirement deadline (today={today:O}): {string.Join(", ", expired)}. Remove the type and its endpoints, then update successors before re-running this gate.");
    }

    [Fact]
    public void SupersedeChainIsAcyclic()
    {
        var byType = EnumerateLifecycles().ToDictionary(static pair => pair.Type, static pair => pair.Lifecycle);

        foreach (var start in byType.Keys)
        {
            var visited = new HashSet<Type>();
            var current = start;

            while (byType.TryGetValue(current, out var lifecycle) && lifecycle.SupersededBy is not null)
            {
                Assert.True(
                    visited.Add(current),
                    $"Supersede chain starting at {start.FullName} contains a cycle (revisited {current.FullName}).");
                current = lifecycle.SupersededBy;
            }
        }
    }

    [Fact]
    public void AtMostOneActiveVersionExistsPerHttpContractBaseName()
    {
        var groups = EnumerateLifecycles()
            .Select(static pair => new
            {
                pair.Type,
                pair.Lifecycle,
                BaseName = ContractLifecyclePolicy.ExtractBaseVersionName(pair.Type.Name)
            })
            .GroupBy(static x => $"{x.Type.Namespace}.{x.BaseName}", StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var activeVersions = group
                .Where(static x => x.Lifecycle.Status == ContractLifecycleStatus.Active)
                .Select(static x => x.Type.FullName)
                .ToArray();

            Assert.True(
                activeVersions.Length <= 1,
                $"More than one Active version exists for base '{group.Key}': {string.Join(", ", activeVersions)}. Mark older versions as Superseded with a retirement deadline.");
        }
    }

    private static IEnumerable<(Type Type, ContractLifecycleAttribute Lifecycle)> EnumerateLifecycles()
    {
        foreach (var type in DiscoverVersionedHttpContracts())
        {
            var lifecycle = type.GetCustomAttribute<ContractLifecycleAttribute>();
            if (lifecycle is not null)
            {
                yield return (type, lifecycle);
            }
        }
    }

    private static IReadOnlyList<Type> DiscoverVersionedHttpContracts()
    {
        var results = new List<Type>();

        foreach (var assembly in RepositoryFiles.ReadModuleApiAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type is { IsClass: true, IsAbstract: false, IsNested: false }
                    && type.Namespace is { } ns
                    && ns.EndsWith(".Api.Contracts", StringComparison.Ordinal)
                    && VersionSuffixPattern.IsMatch(type.Name))
                {
                    results.Add(type);
                }
            }
        }

        return results
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }
}
