using System.Text.RegularExpressions;

namespace Architecture.Tests.Support;

/// <summary>
/// Represents a single handler that satisfies BP-031 through governed named integration coverage
/// rather than a direct <c>{HandlerName}Tests</c> class, together with the justification and the
/// integration test classes that actually cover it.
/// </summary>
/// <param name="Handler">The simple class name of the handler being exempted.</param>
/// <param name="Reason">Human-readable justification surfaced in <c>artifacts/handler-coverage.json</c>.</param>
/// <param name="CoveringTests">
/// One or more integration test class names (simple names, not fully qualified) that exercise the
/// handler through the dispatcher pipeline. The BP-031 guardrail asserts that every name in this
/// list resolves to a real class under <c>tests/Integration.Tests/</c>, so a rename of the covering
/// test breaks the build until the inventory is updated.
/// </param>
/// <param name="TrackedBy">Optional link (issue, ADR, runbook entry) tracking removal of the exemption.</param>
public sealed record HandlerCoverageExemption(
    string Handler,
    string Reason,
    IReadOnlyList<string> CoveringTests,
    string? TrackedBy);

/// <summary>
/// Canonical inventory of handlers that satisfy BP-031 through named integration coverage instead
/// of a direct handler test class. Hoisted out of
/// <c>HandlerTestCoverageGuardrailTests</c> so the debt surface is reviewable without reading
/// C# source and so it can be emitted as a governance artifact.
/// </summary>
/// <remarks>
/// Every exemption must declare the integration test class(es) that exercise the handler. The
/// BP-031 guardrail's <c>Exempted_handlers_must_name_an_existing_covering_integration_test</c>
/// fact verifies the named classes still exist; this prevents the prior failure mode where the
/// inventory's free-form prose could silently rot when an integration test was renamed.
/// </remarks>
public static class HandlerCoverageInventory
{
    private static readonly Regex HandlerClassPattern = new(
        @"class\s+(\w+)\s*(?::\s*)?[^{;]*?\b(?:ICommandHandler|IQueryHandler|IIntegrationEventHandler|IModuleScopedIntegrationEventHandler)\s*<",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly IReadOnlySet<string> ExistingHandlerNames = DiscoverHandlerNames();


    /// <summary>
    /// The canonical, ordered exemption list. Order is stable so the emitted
    /// <c>handler-coverage.json</c> artifact is deterministic under the sort applied
    /// by the guardrail test.
    /// </summary>
    private static IReadOnlyList<HandlerCoverageExemption> AllExemptions { get; } =
    [
    ];

    /// <summary>
    /// Fast lookup of exempted handler names used by the BP-031 guardrail test.
    /// Equivalent in semantics to the previous inline <c>HashSet&lt;string&gt;</c>.
    /// </summary>
    public static IReadOnlyList<HandlerCoverageExemption> Exemptions { get; } =
        AllExemptions
            .Where(exemption => ExistingHandlerNames.Contains(exemption.Handler))
            .ToArray();

    public static IReadOnlySet<string> ExemptedHandlerNames { get; } =
        Exemptions.Select(exemption => exemption.Handler).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> DiscoverHandlerNames()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules");
        var handlerNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in sourceFiles.Where(static path =>
            path.Contains(".Application/", StringComparison.Ordinal)))
        {
            var text = RepositoryFiles.ReadAllText(path);
            foreach (Match match in HandlerClassPattern.Matches(text))
            {
                handlerNames.Add(match.Groups[1].Value);
            }
        }

        return handlerNames;
    }
}
