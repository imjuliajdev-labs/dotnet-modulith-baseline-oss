using System.Text.RegularExpressions;
using BuildingBlocks.Application.Dispatching;

namespace Architecture.Tests;

/// <summary>
/// These guardrails pin the invariants behind <see cref="InboxConsumerName"/>:
/// 1) production code constructs an <see cref="IntegrationEventDeliveryContext"/> in
///    exactly one place (the dispatcher), and
/// 2) production code never calls the test-only <see cref="InboxConsumerName.ForTest"/>
///    factory.
/// Two handlers sharing an inbox consumer name would silently share delivery state —
/// these rules keep the derivation centralised on <see cref="InboxConsumerName.FromHandlerType"/>.
/// </summary>
public sealed class InboxConsumerNameGuardrailTests
{
    private const string DispatcherPath = "src/BuildingBlocks/Application/Dispatching/IntegrationEventDispatcher.cs";

    [Fact]
    public void InboxConsumerNameFromHandlerTypeProducesHandlerFullName()
    {
        var consumerName = InboxConsumerName.FromHandlerType(typeof(SampleHandlerAnchor));

        Assert.Equal(typeof(SampleHandlerAnchor).FullName, consumerName.Value);
    }

    [Fact]
    public void InboxConsumerNameForTestRejectsEmptyAndBlankInput()
    {
        Assert.Throws<ArgumentException>(() => InboxConsumerName.ForTest(string.Empty));
        Assert.Throws<ArgumentException>(() => InboxConsumerName.ForTest("   "));
    }

    [Fact]
    public void InboxConsumerNameForTestRejectsOverlongInput()
    {
        var tooLong = new string('a', InboxConsumerName.MaxLength + 1);

        Assert.Throws<ArgumentException>(() => InboxConsumerName.ForTest(tooLong));
    }

    [Fact]
    public void OnlyTheDispatcherConstructsIntegrationEventDeliveryContext()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src");
        var pattern = new Regex(@"new\s+IntegrationEventDeliveryContext\s*\(", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var relativePath in sourceFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            if (!pattern.IsMatch(source))
            {
                continue;
            }

            if (string.Equals(relativePath, DispatcherPath, StringComparison.Ordinal))
            {
                continue;
            }

            offenders.Add(relativePath);
        }

        Assert.True(
            offenders.Count == 0,
            "IntegrationEventDeliveryContext may only be constructed by IntegrationEventDispatcher. " +
            "Inbox consumer names must be derived from handler types via InboxConsumerName.FromHandlerType. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void ProductionCodeDoesNotCallInboxConsumerNameForTest()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src");
        var offenders = new List<string>();

        foreach (var relativePath in sourceFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            if (source.Contains("InboxConsumerName.ForTest", StringComparison.Ordinal))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.Empty(offenders);
    }

    private sealed class SampleHandlerAnchor;
}
