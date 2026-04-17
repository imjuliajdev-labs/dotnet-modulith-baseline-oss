using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class DispatcherCancellationContractTests
{
    // Matches a `next(` call followed immediately by `)` with only whitespace inside, e.g. `next()` or `next( )`.
    // The cancellation contract requires every behavior to forward an explicit token to the next stage of the pipeline.
    private static readonly Regex ZeroArgNextInvocation = new(@"\bnext\s*\(\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void PipelineBehaviorsForwardCancellationTokenWhenInvokingNext()
    {
        var behaviorFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "BuildingBlocks", "Application", "Dispatching", "Behaviors");

        Assert.NotEmpty(behaviorFiles);

        var offenders = new List<string>();
        foreach (var relativePath in behaviorFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            if (ZeroArgNextInvocation.IsMatch(source))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Pipeline behaviors must forward an explicit cancellation token to next(...). Offenders: "
                + string.Join(", ", offenders));
    }

    [Fact]
    public void RequestHandlerDelegateAcceptsCancellationToken()
    {
        var contractsSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/RequestContracts.cs");

        Assert.Contains(
            "public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);",
            contractsSource,
            StringComparison.Ordinal);
    }
}
