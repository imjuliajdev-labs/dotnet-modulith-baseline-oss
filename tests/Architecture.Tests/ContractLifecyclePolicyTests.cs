using BuildingBlocks.Domain.Contracts;

namespace Architecture.Tests;

public sealed class ContractLifecyclePolicyTests
{
    private static readonly DateOnly ReferenceToday = new(2026, 04, 10);

    [Fact]
    public void IsPastRetirementDeadline_ReturnsTrue_WhenSupersededContractRetireOnIsBeforeToday()
    {
        var lifecycle = new ContractLifecycleAttribute("2025-01-01", ContractLifecycleStatus.Superseded)
        {
            SupersededBy = typeof(SyntheticSuccessor),
            RetireOn = "2026-04-09"
        };

        Assert.True(ContractLifecyclePolicy.IsPastRetirementDeadline(lifecycle, ReferenceToday));
    }

    [Fact]
    public void IsPastRetirementDeadline_ReturnsFalse_WhenRetireOnEqualsToday()
    {
        var lifecycle = new ContractLifecycleAttribute("2025-01-01", ContractLifecycleStatus.Superseded)
        {
            SupersededBy = typeof(SyntheticSuccessor),
            RetireOn = "2026-04-10"
        };

        Assert.False(ContractLifecyclePolicy.IsPastRetirementDeadline(lifecycle, ReferenceToday));
    }

    [Fact]
    public void IsPastRetirementDeadline_ReturnsFalse_WhenRetireOnIsAfterToday()
    {
        var lifecycle = new ContractLifecycleAttribute("2025-01-01", ContractLifecycleStatus.Superseded)
        {
            SupersededBy = typeof(SyntheticSuccessor),
            RetireOn = "2027-01-15"
        };

        Assert.False(ContractLifecyclePolicy.IsPastRetirementDeadline(lifecycle, ReferenceToday));
    }

    [Fact]
    public void IsPastRetirementDeadline_ReturnsFalse_ForActiveContracts_RegardlessOfRetireOnValue()
    {
        var lifecycle = new ContractLifecycleAttribute("2025-01-01", ContractLifecycleStatus.Active);

        Assert.False(ContractLifecyclePolicy.IsPastRetirementDeadline(lifecycle, ReferenceToday));
    }

    [Fact]
    public void IsPastRetirementDeadline_ReturnsFalse_WhenSupersededContractHasUnparseableRetireOn()
    {
        var lifecycle = new ContractLifecycleAttribute("2025-01-01", ContractLifecycleStatus.Superseded)
        {
            SupersededBy = typeof(SyntheticSuccessor),
            RetireOn = "not-a-date"
        };

        Assert.False(ContractLifecyclePolicy.IsPastRetirementDeadline(lifecycle, ReferenceToday));
    }

    [Fact]
    public void TryParseIsoDate_ParsesValidIsoLocalDate()
    {
        Assert.True(ContractLifecyclePolicy.TryParseIsoDate("2026-04-10", out var parsed));
        Assert.Equal(new DateOnly(2026, 04, 10), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026/04/10")]
    [InlineData("10-04-2026")]
    [InlineData("2026-4-10")]
    public void TryParseIsoDate_RejectsNonIsoStrings(string? value)
    {
        Assert.False(ContractLifecyclePolicy.TryParseIsoDate(value, out _));
    }

    [Theory]
    [InlineData("FooV1", "Foo")]
    [InlineData("FooV2", "Foo")]
    [InlineData("KnowledgeEntryPublishedEventV10", "KnowledgeEntryPublishedEvent")]
    [InlineData("NoVersionSuffix", "NoVersionSuffix")]
    [InlineData("AlreadyNamedV", "AlreadyNamedV")]
    public void ExtractBaseVersionName_StripsTrailingVersion(string typeName, string expected)
    {
        Assert.Equal(expected, ContractLifecyclePolicy.ExtractBaseVersionName(typeName));
    }

    private sealed class SyntheticSuccessor;
}
