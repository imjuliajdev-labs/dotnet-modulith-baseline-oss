using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class HandlerExceptionGuardrailTests
{
    private static readonly Regex HandlerTypePattern = new(
        @"class\s+\w+\s*:\s*[^\r\n{;]*\b(ICommandHandler|IQueryHandler|IIntegrationEventHandler)\s*<",
        RegexOptions.CultureInvariant);

    private static readonly Regex BroadExceptionCatchPattern = new(
        @"catch\s*\(\s*Exception(?:\s+\w+)?\s*\)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void ModuleRequestAndEventHandlersDoNotCatchBroadException()
    {
        var violatingFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains("/Application/", StringComparison.Ordinal))
            .Where(path =>
            {
                var text = RepositoryFiles.ReadAllText(path);
                return HandlerTypePattern.IsMatch(text) && BroadExceptionCatchPattern.IsMatch(text);
            })
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violatingFiles.Length == 0,
            $"Command, query, and integration-event handlers must not catch broad Exception. Violations: {string.Join(", ", violatingFiles)}");
    }
}
