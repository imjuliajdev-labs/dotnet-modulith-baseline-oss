namespace BuildingBlocks.Application.Dispatching;

internal sealed class NoOpCommandTransactionScopeFactory : ICommandTransactionScopeFactory
{
    public ValueTask<ICommandTransactionScope> BeginAsync(CommandTransactionContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<ICommandTransactionScope>(NoOpCommandTransactionScope.Instance);
    }

    private sealed class NoOpCommandTransactionScope : ICommandTransactionScope
    {
        public static NoOpCommandTransactionScope Instance { get; } = new();

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
